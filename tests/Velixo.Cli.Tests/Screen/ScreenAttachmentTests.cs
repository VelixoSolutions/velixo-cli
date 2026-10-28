using System.Net;
using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
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
            "command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--attach", $"{file}:Signed quote 4471", "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["get_upload_url", "execute_commands"], test.Mcp.Calls.Select(call => call.Name));
        Assert.Equal("%PDF-1.7 quote", Assert.Single(test.Http.Requests).Body);

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

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--attach", file, "--yes", "--intent", ScreenHarness.Intent);

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

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--download-dir", directory, "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        JsonArray attachments = test.Output["result"]!["attachments"]!.AsArray();
        Assert.Equal(Path.Combine(directory, "SO641010.pdf"), (string?)attachments[0]!["path"]);
        Assert.Equal(Path.Combine(directory, "SO641010 (2).pdf"), (string?)attachments[1]!["path"]);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(directory, "SO641010.pdf")));
        Assert.Equal([4, 5], File.ReadAllBytes(Path.Combine(directory, "SO641010 (2).pdf")));
        Assert.Equal(["https://nx-dev.velixo.com/mcp/download/a", "https://nx-dev.velixo.com/mcp/download/b"], test.Http.Requests.Select(request => request.Uri.ToString()));
    }

    [Fact]
    public async Task A_failed_download_after_the_batch_ran_says_not_to_run_it_again()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("read.batch.json", ScreenHarness.ReadBatch);
        test.Mcp.ThenText(ScreenHarness.Done(beside: """
            "attachments": [ { "kind": "report", "fileName": "SO641010.pdf", "downloadUrl": "https://nx-dev.velixo.com/mcp/download/a" } ]
            """));
        test.Http.ThenStatus(HttpStatusCode.NotFound);

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--download-dir", "out", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal(ErrorCodes.AttachmentTokenInvalid, (string?)test.Error["code"]);
        Assert.StartsWith("Do not run the batch again", (string?)test.Error["nextStep"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_dir_resolves_against_the_working_directory_and_keeps_an_existing_file()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("read.batch.json", ScreenHarness.ReadBatch);
        Directory.CreateDirectory(Path.Combine(test.Directory, "out"));
        test.Write(Path.Combine("out", "SO641010.pdf"), "first");
        test.Mcp.ThenText(ScreenHarness.Done(beside: """
            "attachments": [ { "kind": "report", "fileName": "SO641010.pdf", "downloadUrl": "https://nx-dev.velixo.com/mcp/download/a" } ]
            """));
        test.Http.Then(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--download-dir", "out", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(Path.Combine(test.Directory, "out", "SO641010 (2).pdf"), (string?)test.Output["result"]!["attachments"]![0]!["path"]);
        Assert.Equal("first", File.ReadAllText(Path.Combine(test.Directory, "out", "SO641010.pdf")));
    }

    [Fact]
    public void Attach_splits_the_comment_on_the_last_colon()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "a.pdf"), "x");

        ScreenAttachment spec = ScreenCommands.ParseAttachment("a.pdf:Signed quote", directory.Path);
        ScreenAttachment empty = ScreenCommands.ParseAttachment("a.pdf:", directory.Path);

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

        ScreenAttachment drive = ScreenCommands.ParseAttachment(@"C:\scan.pdf", directory.Path);
        ScreenAttachment driveWithComment = ScreenCommands.ParseAttachment(@"C:\scan.pdf:Scan", directory.Path);
        ScreenAttachment plain = ScreenCommands.ParseAttachment("b.pdf", directory.Path);

        Assert.Null(drive.Comment);
        Assert.Equal("Scan", driveWithComment.Comment);
        Assert.Null(plain.Comment);
        Assert.Throws<CliException>(() => ScreenCommands.ParseAttachment("b.pdf:/elsewhere", directory.Path));
        Assert.Contains("'C:report.pdf'", Assert.Throws<CliException>(() => ScreenCommands.ParseAttachment("C:report.pdf", directory.Path)).Message, StringComparison.Ordinal);
    }
}
