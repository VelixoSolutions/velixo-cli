using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using Velixo.Cli.Screen;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests.Screen;

public sealed class ScreenAttachmentTests
{
    [Fact]
    public async Task Attach_stages_each_file_and_passes_its_token()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        var file = test.Write("quote.pdf", "%PDF-1.7 quote");
        test.Mcp
            .ThenText("""{"fileToken":"ft_1","uploadUrl":"https://nx-dev.velixo.com/mcp/upload/secret-1","expiresAt":"2026-10-28T15:00:00Z"}""")
            .ThenText(ScreenHarness.Done());
        test.Http.ThenStatus(HttpStatusCode.OK);

        var exit = await test.RunAsync(
            "screen", "run", "--batch", batch, "--var", "CustomerID=ACME", "--attach", $"{file}:Signed quote 4471", "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        (var name, JsonObject staging) = test.Mcp.Calls[0];
        Assert.Equal("get_upload_url", name);
        Assert.Equal("quote.pdf", (string?)staging["fileName"]);
        Assert.Equal(14, (long)staging["fileSizeBytes"]!);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("%PDF-1.7 quote"))), (string?)staging["sha256"]);

        RecordedRequest put = Assert.Single(test.Http.Requests);
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("https://nx-dev.velixo.com/mcp/upload/secret-1", put.Uri.ToString());
        Assert.Equal("%PDF-1.7 quote", put.Body);

        JsonNode attachment = Assert.Single(test.Executed["attachments"]!.AsArray())!;
        Assert.Equal("quote.pdf", (string?)attachment["fileName"]);
        Assert.Equal("ft_1", (string?)attachment["fileToken"]);
        Assert.Equal("Signed quote 4471", (string?)attachment["comment"]);
    }

    [Fact]
    public async Task A_refused_upload_stops_before_the_batch_and_hides_the_link()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        var file = test.Write("quote.pdf", "%PDF");
        test.Mcp.ThenText("""{"fileToken":"ft_1","uploadUrl":"https://nx-dev.velixo.com/mcp/upload/secret-1"}""");
        test.Http.ThenStatus(HttpStatusCode.BadGateway);

        var exit = await test.RunAsync("screen", "run", "--batch", batch, "--var", "CustomerID=ACME", "--attach", file, "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Failure, exit);
        Assert.DoesNotContain("secret-1", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Single(test.Mcp.Calls);
    }

    [Fact]
    public async Task Download_dir_saves_every_attachment_and_adds_its_path()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("read.batch.json", ScreenHarness.ReadBatch);
        var directory = Path.Combine(test.Directory, "out");
        test.Mcp.ThenText(ScreenHarness.Done(beside: """
            "attachments": [
              { "kind": "report", "fileName": "SO641010.pdf", "downloadUrl": "https://nx-dev.velixo.com/mcp/download/a" },
              { "kind": "erpAttachment", "fileName": "../SO641010.pdf", "downloadUrl": "https://nx-dev.velixo.com/mcp/download/b" }
            ]
            """));
        test.Http
            .Then(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) })
            .Then(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([4, 5]) });

        var exit = await test.RunAsync("screen", "run", "--batch", batch, "--download-dir", directory, "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        JsonArray attachments = test.Output["result"]!["attachments"]!.AsArray();
        Assert.Equal(Path.Combine(directory, "SO641010.pdf"), (string?)attachments[0]!["path"]);
        Assert.Equal(Path.Combine(directory, "SO641010 (2).pdf"), (string?)attachments[1]!["path"]);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(directory, "SO641010.pdf")));
        Assert.Equal([4, 5], File.ReadAllBytes(Path.Combine(directory, "SO641010 (2).pdf")));
        Assert.Equal(["https://nx-dev.velixo.com/mcp/download/a", "https://nx-dev.velixo.com/mcp/download/b"], test.Http.Requests.Select(request => request.Uri.ToString()));
    }

    [Fact]
    public void Attach_splits_the_comment_on_the_last_colon()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "a.pdf"), "x");

        ScreenAttachmentSpec spec = ScreenAttachmentSpec.Parse("a.pdf:Signed quote", directory.Path);
        ScreenAttachmentSpec empty = ScreenAttachmentSpec.Parse("a.pdf:", directory.Path);

        Assert.Equal(Path.Combine(directory.Path, "a.pdf"), spec.Path);
        Assert.Equal("a.pdf", spec.FileName);
        Assert.Equal(1, spec.SizeBytes);
        Assert.Equal("Signed quote", spec.Comment);
        Assert.Null(empty.Comment);
    }

    [Fact]
    public void Attach_keeps_a_colon_that_belongs_to_the_path()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, @"C:\scan.pdf"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "b.pdf"), "x");

        ScreenAttachmentSpec drive = ScreenAttachmentSpec.Parse(@"C:\scan.pdf", directory.Path);
        ScreenAttachmentSpec driveWithComment = ScreenAttachmentSpec.Parse(@"C:\scan.pdf:Scan", directory.Path);
        ScreenAttachmentSpec plain = ScreenAttachmentSpec.Parse("b.pdf", directory.Path);

        Assert.Null(drive.Comment);
        Assert.Equal("Scan", driveWithComment.Comment);
        Assert.Null(plain.Comment);
        Assert.Throws<CliException>(() => ScreenAttachmentSpec.Parse("b.pdf:/elsewhere", directory.Path));
        Assert.Contains("'C:report.pdf'", Assert.Throws<CliException>(() => ScreenAttachmentSpec.Parse("C:report.pdf", directory.Path)).Message, StringComparison.Ordinal);
    }
}
