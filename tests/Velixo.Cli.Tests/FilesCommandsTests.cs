using System.Net;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class FilesCommandsTests : IDisposable
{
    private readonly TestServices _test = new();
    private readonly FakeMcpClient _mcp = new();

    public FilesCommandsTests()
    {
        _test.Services.McpClientFactory = _ => _mcp;
    }

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task Download_from_a_saved_result_finds_nested_attachments_once_each()
    {
        var result = WriteFile(
            "run.json",
            """
            {
              "taskId": "t_1",
              "status": "done",
              "result": {
                "data": { "ok": true },
                "attachments": [
                  { "fileName": "Trial Balance.pdf", "downloadUrl": "https://nx-dev.velixo.com/mcp/download/rpt_1" }
                ]
              },
              "runs": [
                { "result": { "attachments": [
                  { "downloadUrl": "https://nx-dev.velixo.com/mcp/download/dl_2" },
                  { "downloadUrl": "https://nx-dev.velixo.com/mcp/download/rpt_1" },
                  { "fileName": "no link" }
                ] } }
              ]
            }
            """);
        var target = Path.Combine(_test.Directory, "saved");
        _test.Http
            .Then(_ => Served("pdf bytes", "trial-balance.pdf"))
            .Then(_ => Served("attachment bytes", "scan.png"));

        var exit = await _test.RunAsync("files", "download", "--from", result, "--dir", target);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            ["https://nx-dev.velixo.com/mcp/download/rpt_1", "https://nx-dev.velixo.com/mcp/download/dl_2"],
            _test.Http.Requests.Select(request => request.Uri.ToString()));
        JsonArray saved = JsonNode.Parse(_test.Console.StdOut)!.AsArray();
        Assert.Equal(["trial-balance.pdf", "scan.png"], saved.Select(file => (string?)file!["fileName"]));
        Assert.Equal(Path.Combine(target, "trial-balance.pdf"), (string?)saved[0]!["path"]);
        Assert.Equal(9, (int?)saved[0]!["bytes"]);
        Assert.Equal("application/octet-stream", (string?)saved[0]!["contentType"]);
        Assert.Equal("attachment bytes", await File.ReadAllTextAsync(Path.Combine(target, "scan.png"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Download_to_an_output_path_takes_one_link()
    {
        var outPath = Path.Combine(_test.Directory, "report.pdf");
        _test.Http.Then(_ => Served("bytes", "ignored.pdf"));

        var exit = await _test.RunAsync("files", "download", "https://nx-dev.velixo.com/mcp/download/rpt_1", "--out", outPath);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("bytes", await File.ReadAllTextAsync(outPath, TestContext.Current.CancellationToken));
        Assert.Equal(outPath, (string?)JsonNode.Parse(_test.Console.StdOut)![0]!["path"]);
    }

    [Fact]
    public async Task Download_with_out_and_two_links_is_exit_2_before_any_request()
    {
        var exit = await _test.RunAsync(
            "files",
            "download",
            "https://nx-dev.velixo.com/mcp/download/rpt_1",
            "https://nx-dev.velixo.com/mcp/download/rpt_2",
            "--out",
            Path.Combine(_test.Directory, "one.pdf"));

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_test.Http.Requests);
    }

    [Fact]
    public async Task Download_with_no_link_is_exit_2()
    {
        var exit = await _test.RunAsync("files", "download");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_test.Http.Requests);
    }

    [Fact]
    public async Task Download_from_a_result_without_attachments_is_exit_2()
    {
        var result = WriteFile("empty.json", """{ "result": { "data": {} } }""");

        var exit = await _test.RunAsync("files", "download", "--from", result);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_test.Http.Requests);
    }

    [Fact]
    public async Task Download_of_an_expired_link_is_exit_5_ATTACHMENT_TOKEN_INVALID()
    {
        _test.Http.Then(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("This download link is no longer valid.") });

        var exit = await _test.RunAsync("files", "download", "https://nx-dev.velixo.com/mcp/download/rpt_old", "--dir", _test.Directory);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal("ATTACHMENT_TOKEN_INVALID", (string?)JsonNode.Parse(_test.Console.StdOut)!["error"]!["code"]);
    }

    private static HttpResponseMessage Served(string content, string fileName)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content)) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        response.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = fileName };
        return response;
    }

    private static string Link(string token) =>
        $$"""{"fileToken":"{{token}}","uploadUrl":"https://nx-dev.velixo.com/mcp/upload/{{token}}","expiresAt":"2026-10-28T16:10:00+00:00"}""";

    private static string Staged(bool submitted, int files, string? note = null)
    {
        var listing = new JsonObject
        {
            ["maxFiles"] = 5,
            ["submitted"] = submitted,
            ["expiresAt"] = "2026-10-28T16:10:00+00:00",
            ["note"] = note,
            ["files"] = new JsonArray(Enumerable.Range(1, files)
                .Select(index => (JsonNode?)new JsonObject
                {
                    ["fileName"] = $"file-{index}.pdf",
                    ["sizeBytes"] = 100 * index,
                    ["fileToken"] = $"ut_{index}",
                    ["alreadyFiled"] = false,
                })
                .ToArray()),
        };
        return listing.ToJsonString();
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_test.Directory, name);
        File.WriteAllText(path, content);
        return path;
    }
}
