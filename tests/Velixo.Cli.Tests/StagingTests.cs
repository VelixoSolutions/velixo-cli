using System.Net;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Screen;

namespace Velixo.Cli.Tests;

public sealed class StagingTests
{
    private const string Link = """{"fileToken":"ft_1","uploadUrl":"https://nx-dev.velixo.com/mcp/upload/secret-1","expiresAt":"2999-01-01T00:00:00Z"}""";

    [Fact]
    public async Task Stage_uploads_a_local_file_prints_its_token_and_keeps_it_for_the_profile()
    {
        using var test = new ScreenHarness();
        var file = test.Write("quote.pdf", "%PDF-1.7 quote");
        test.Mcp.ThenText(Link);
        test.Http.ThenStatus(HttpStatusCode.OK);

        var exit = await test.RunAsync("files", "stage", file);

        Assert.Equal(ExitCodes.Success, exit);
        JsonObject sent = Assert.Single(test.Mcp.Calls).Arguments;
        Assert.Equal("quote.pdf", (string?)sent["fileName"]);
        Assert.Equal(14, (long)sent["fileSizeBytes"]!);
        Assert.Equal(64, ((string?)sent["sha256"])!.Length);
        Assert.Equal("%PDF-1.7 quote", Assert.Single(test.Http.Requests).Body);
        Assert.Equal("ft_1", (string?)test.Output["fileToken"]);
        Assert.True((bool)test.Output["uploaded"]!);
        Assert.DoesNotContain("secret-1", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Equal("quote.pdf", Assert.Single(test.Profiles.LoadStagedFiles("prod").Files).FileName);
    }

    [Fact]
    public async Task Stage_with_a_size_hands_out_the_upload_url_and_the_curl_command_without_uploading()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText(Link);

        var exit = await test.RunAsync("files", "stage", "scan.pdf", "--size", "48213");

        Assert.Equal(ExitCodes.Success, exit);
        JsonObject sent = Assert.Single(test.Mcp.Calls).Arguments;
        Assert.Equal(48213, (long)sent["fileSizeBytes"]!);
        Assert.False(sent.ContainsKey("sha256"));
        Assert.Empty(test.Http.Requests);
        Assert.False((bool)test.Output["uploaded"]!);
        Assert.Equal("https://nx-dev.velixo.com/mcp/upload/secret-1", (string?)test.Output["uploadUrl"]);
        Assert.Equal("curl -T 'scan.pdf' 'https://nx-dev.velixo.com/mcp/upload/secret-1'", (string?)test.Output["curl"]);
    }

    [Fact]
    public async Task Stage_with_no_upload_sends_the_checksum_and_names_the_local_path_in_the_curl_command()
    {
        using var test = new ScreenHarness();
        var file = test.Write("quote.pdf", "%PDF");
        test.Mcp.ThenText(Link);

        await test.RunAsync("files", "stage", file, "--no-upload");

        Assert.True(test.Mcp.Calls[0].Arguments.ContainsKey("sha256"));
        Assert.Empty(test.Http.Requests);
        Assert.StartsWith($"curl -T '{file}' ", (string?)test.Output["curl"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Attach_token_files_a_staged_file_under_its_name_without_staging_it_again()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        test.Mcp.ThenText(Link).ThenText(ScreenHarness.Done());
        await test.RunAsync("files", "stage", "quote.pdf", "--size", "4");

        var exit = await test.RunAsync(
            "command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--attach-token", "ft_1", "--attach-comment", "Signed quote", "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["get_upload_url", "execute_commands"], test.Mcp.Calls.Select(call => call.Name));
        JsonNode attachment = Assert.Single(test.Executed["attachments"]!.AsArray())!;
        Assert.Equal("quote.pdf", (string?)attachment["fileName"]);
        Assert.Equal("ft_1", (string?)attachment["fileToken"]);
        Assert.Equal("Signed quote", (string?)attachment["comment"]);
    }

    [Fact]
    public async Task A_token_with_a_colon_in_it_is_found_whole()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        test.Mcp
            .ThenText("""{"fileToken":"ft:a1","uploadUrl":"https://nx-dev.velixo.com/mcp/upload/s","expiresAt":"2999-01-01T00:00:00Z"}""")
            .ThenText(ScreenHarness.Done());
        await test.RunAsync("files", "stage", "quote.pdf", "--size", "4");

        await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--attach-token", "ft:a1", "--attach-comment", "Signed: quote", "--yes", "--intent", ScreenHarness.Intent);

        JsonNode attachment = Assert.Single(test.Executed["attachments"]!.AsArray())!;
        Assert.Equal("ft:a1", (string?)attachment["fileToken"]);
        Assert.Equal("Signed: quote", (string?)attachment["comment"]);
    }

    [Fact]
    public async Task The_curl_command_quotes_a_name_and_a_url_the_shell_would_otherwise_split_or_expand()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText("""{"fileToken":"ft_1","uploadUrl":"https://nx-dev.velixo.com/mcp/upload/a$b","expiresAt":"2999-01-01T00:00:00Z"}""");

        await test.RunAsync("files", "stage", "AT&T;invoice $1 'final'.pdf", "--size", "4");

        Assert.Equal("curl -T 'AT&T;invoice $1 '\\''final'\\''.pdf' 'https://nx-dev.velixo.com/mcp/upload/a$b'", (string?)test.Output["curl"]);
    }

    [Fact]
    public async Task On_windows_the_curl_command_is_for_powershell()
    {
        using var test = new ScreenHarness();
        test.Environment.IsWindows = true;
        test.Mcp.ThenText(Link);

        await test.RunAsync("files", "stage", "it's.pdf", "--size", "4");

        Assert.Equal("curl.exe -T 'it''s.pdf' 'https://nx-dev.velixo.com/mcp/upload/secret-1'", (string?)test.Output["curl"]);
    }

    [Theory]
    [InlineData("scan.pdf", "--size", "4", "--sha256", "not-a-hash")]
    [InlineData("folder/", "--size", "4")]
    [InlineData("scan.pdf", "--size", "0")]
    public async Task A_bad_name_size_or_checksum_is_exit_2_before_any_call(params string[] args)
    {
        using var test = new ScreenHarness();

        var exit = await test.RunAsync(["files", "stage", .. args]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_token_this_profile_did_not_stage_is_exit_2_before_any_call()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--attach-token", "ft_unknown", "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(test.Mcp.Calls);
    }
}
