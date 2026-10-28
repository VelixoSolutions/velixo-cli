using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Velixo.Cli.Files;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class FileTransferTests : IDisposable
{
    private const string UploadLink = """{"fileToken":"ut_abc","uploadUrl":"https://nx-dev.velixo.com/mcp/upload/ut_abc","expiresAt":"2026-10-28T16:10:00+00:00"}""";

    private readonly TempDirectory _directory = new();
    private readonly FakeMcpClient _mcp = new();
    private readonly FakeHttpHandler _handler = new();
    private readonly HttpClient _http;

    public FileTransferTests()
    {
        _http = new HttpClient(_handler);
    }

    public void Dispose()
    {
        _http.Dispose();
        _directory.Dispose();
    }

    [Fact]
    public async Task Staging_declares_the_size_and_sha256_and_streams_the_bytes_to_the_upload_url()
    {
        var bytes = Encoding.UTF8.GetBytes("invoice 4411, total 183.22");
        var path = Path.Combine(_directory.Path, "invoice-4411.pdf");
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        string? contentType = null;
        long? contentLength = null;
        _mcp.ThenText(UploadLink);
        _handler.Then(request =>
        {
            contentType = request.Content!.Headers.ContentType?.MediaType;
            contentLength = request.Content.Headers.ContentLength;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Uploaded 'invoice-4411.pdf'.") };
        });

        StagedFile staged = await Transfer().StageAsync(path, "Vendor quote 4471", TestContext.Current.CancellationToken);

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.Equal(new StagedFile("invoice-4411.pdf", bytes.Length, sha256, "ut_abc", "2026-10-28T16:10:00+00:00", "Vendor quote 4471"), staged);

        (var tool, JsonObject arguments) = Assert.Single(_mcp.Calls);
        Assert.Equal(McpToolNames.GetUploadUrl, tool);
        Assert.Equal("invoice-4411.pdf", (string?)arguments["fileName"]);
        Assert.Equal(bytes.Length, (long?)arguments["fileSizeBytes"]);
        Assert.Equal(sha256, (string?)arguments["sha256"]);
        Assert.False(arguments.ContainsKey("intent"));
        Assert.False(arguments.ContainsKey("maxFiles"));

        RecordedRequest put = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("https://nx-dev.velixo.com/mcp/upload/ut_abc", put.Uri.ToString());
        Assert.Equal("invoice 4411, total 183.22", put.Body);
        Assert.Equal("application/octet-stream", contentType);
        Assert.Equal(bytes.Length, contentLength);
        Assert.Null(put.Header("Authorization"));
    }

    [Fact]
    public async Task A_refused_upload_is_exit_5_ATTACHMENT_UPLOAD_FAILED_with_the_http_status()
    {
        var path = Path.Combine(_directory.Path, "quote.pdf");
        await File.WriteAllTextAsync(path, "quote", TestContext.Current.CancellationToken);
        _mcp.ThenText(UploadLink);
        _handler.Then(_ => new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("This upload link already took a file.") });

        CliException error = await Assert.ThrowsAsync<CliException>(() => Transfer().StageAsync(path, null, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.ServerRefused, error.ExitCode);
        Assert.Equal("ATTACHMENT_UPLOAD_FAILED", error.Code);
        Assert.Contains("HTTP 409", error.Message, StringComparison.Ordinal);
        Assert.Contains("already took a file", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_the_server_will_not_stage_is_exit_5_with_its_code_and_nothing_is_uploaded()
    {
        var path = Path.Combine(_directory.Path, "tool.exe");
        await File.WriteAllTextAsync(path, "MZ", TestContext.Current.CancellationToken);
        _mcp.ThenText("ATTACHMENT_TYPE_NOT_ALLOWED: 'tool.exe' is not a file type this server will attach.", isError: true);

        CliException error = await Assert.ThrowsAsync<CliException>(() => Transfer().StageAsync(path, null, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.ServerRefused, error.ExitCode);
        Assert.Equal("ATTACHMENT_TYPE_NOT_ALLOWED", error.Code);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task A_missing_file_is_a_usage_error_before_any_call()
    {
        CliException error = await Assert.ThrowsAsync<CliException>(
            () => Transfer().StageAsync(Path.Combine(_directory.Path, "missing.pdf"), null, TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Usage, error.ExitCode);
        Assert.Empty(_mcp.Calls);
    }

    [Fact]
    public async Task A_download_takes_its_name_from_Content_Disposition()
    {
        _handler.Then(_ => Served("%PDF-1.7 report", "application/pdf", disposition: "report-2026-09.pdf"));

        DownloadedFile file = await Transfer().DownloadAsync(
            "https://nx-dev.velixo.com/mcp/download/rpt_Abc123",
            _directory.Path,
            outPath: null,
            fileName: null,
            TestContext.Current.CancellationToken);

        var expected = Path.Combine(_directory.Path, "report-2026-09.pdf");
        Assert.Equal(new DownloadedFile("report-2026-09.pdf", expected, 15, "application/pdf"), file);
        Assert.Equal("%PDF-1.7 report", await File.ReadAllTextAsync(expected, TestContext.Current.CancellationToken));
        Assert.Null(Assert.Single(_handler.Requests).Header("Authorization"));
    }

    [Fact]
    public async Task A_download_without_Content_Disposition_takes_its_name_from_the_url()
    {
        _handler.Then(_ => Served("a,b", "text/csv", disposition: null));

        DownloadedFile file = await Transfer().DownloadAsync(
            "https://nx-dev.velixo.com/mcp/download/open%20items.csv?x=1",
            _directory.Path,
            outPath: null,
            fileName: null,
            TestContext.Current.CancellationToken);

        Assert.Equal("open items.csv", file.FileName);
        Assert.Equal(Path.Combine(_directory.Path, "open items.csv"), file.Path);
    }

    [Fact]
    public async Task A_name_with_a_directory_part_cannot_leave_the_target_directory()
    {
        _handler.Then(_ => Served("x", "text/plain", disposition: "..\\..\\escape.txt"));
        var target = Path.Combine(_directory.Path, "downloads");

        DownloadedFile file = await Transfer().DownloadAsync(
            "https://nx-dev.velixo.com/mcp/download/dl_1",
            target,
            outPath: null,
            fileName: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(target, "escape.txt"), file.Path);
        Assert.False(File.Exists(Path.Combine(_directory.Path, "escape.txt")));
    }

    [Fact]
    public async Task A_download_into_a_directory_keeps_a_file_that_is_already_there()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory.Path, "report.pdf"), "first", TestContext.Current.CancellationToken);
        _handler.Then(_ => Served("second", "application/pdf", disposition: "report.pdf"));

        DownloadedFile file = await Transfer().DownloadAsync(
            "https://nx-dev.velixo.com/mcp/download/rpt_2",
            _directory.Path,
            outPath: null,
            fileName: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_directory.Path, "report (2).pdf"), file.Path);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(_directory.Path, "report.pdf"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_download_to_an_output_path_creates_its_directory_and_replaces_the_file()
    {
        var outPath = Path.Combine(_directory.Path, "out", "latest.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        await File.WriteAllTextAsync(outPath, "old", TestContext.Current.CancellationToken);
        _handler.Then(_ => Served("new", "application/pdf", disposition: "ignored.pdf"));

        DownloadedFile file = await Transfer().DownloadAsync(
            "https://nx-dev.velixo.com/mcp/download/rpt_3",
            directory: null,
            outPath,
            fileName: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(outPath, file.Path);
        Assert.Equal("new", await File.ReadAllTextAsync(outPath, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(outPath + ".part"));
    }

    [Fact]
    public async Task A_4xx_from_the_link_is_exit_5_ATTACHMENT_TOKEN_INVALID_and_leaves_no_file()
    {
        _handler.Then(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("This download link is no longer valid.") });

        CliException error = await Assert.ThrowsAsync<CliException>(() => Transfer().DownloadAsync(
            "https://nx-dev.velixo.com/mcp/download/rpt_old",
            _directory.Path,
            outPath: null,
            fileName: null,
            TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.ServerRefused, error.ExitCode);
        Assert.Equal("ATTACHMENT_TOKEN_INVALID", error.Code);
        Assert.Contains("HTTP 404", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("rpt_old", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(_directory.Path));
    }

    [Fact]
    public async Task A_5xx_from_the_link_is_exit_1()
    {
        _handler.ThenStatus(HttpStatusCode.BadGateway);

        CliException error = await Assert.ThrowsAsync<CliException>(() => Transfer().DownloadAsync(
            "https://nx-dev.velixo.com/mcp/download/rpt_1",
            _directory.Path,
            outPath: null,
            fileName: null,
            TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Failure, error.ExitCode);
        Assert.Equal(ErrorCodes.Http, error.Code);
    }

    [Fact]
    public async Task A_link_that_is_not_an_http_url_is_a_usage_error()
    {
        CliException error = await Assert.ThrowsAsync<CliException>(() => Transfer().DownloadAsync(
            "file:///etc/passwd",
            _directory.Path,
            outPath: null,
            fileName: null,
            TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Usage, error.ExitCode);
        Assert.Empty(_handler.Requests);
    }

    private static HttpResponseMessage Served(string content, string contentType, string? disposition)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content)) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        if (disposition is not null)
        {
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = disposition };
        }

        return response;
    }

    private FileTransfer Transfer() => new(_mcp, _http);
}
