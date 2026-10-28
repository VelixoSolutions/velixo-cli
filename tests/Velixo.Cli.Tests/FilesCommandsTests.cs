using System.Net;
using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
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
    public async Task Upload_stages_every_path_and_prints_one_entry_each()
    {
        var first = WriteFile("invoice-1.pdf", "one");
        var second = WriteFile("invoice-2.pdf", "second file");
        _mcp.ThenText(Link("ut_1")).ThenText(Link("ut_2"));
        _test.Http.ThenStatus(HttpStatusCode.OK).ThenStatus(HttpStatusCode.OK);

        var exit = await _test.RunAsync("files", "upload", first, second, "--comment", "Vendor quotes");

        Assert.Equal(ExitCodes.Success, exit);
        JsonArray staged = JsonNode.Parse(_test.Console.StdOut)!.AsArray();
        Assert.Equal(2, staged.Count);
        Assert.Equal("invoice-1.pdf", (string?)staged[0]!["fileName"]);
        Assert.Equal(3, (int?)staged[0]!["sizeBytes"]);
        Assert.Equal("ut_1", (string?)staged[0]!["fileToken"]);
        Assert.Equal(64, ((string?)staged[0]!["sha256"])!.Length);
        Assert.Equal("Vendor quotes", (string?)staged[0]!["comment"]);
        Assert.Equal("invoice-2.pdf", (string?)staged[1]!["fileName"]);
        Assert.Equal("ut_2", (string?)staged[1]!["fileToken"]);
        Assert.Equal([McpToolNames.GetUploadUrl, McpToolNames.GetUploadUrl], _mcp.Calls.Select(call => call.Name));
        Assert.Equal(["PUT", "PUT"], _test.Http.Requests.Select(request => request.Method.Method));
        Assert.Equal("second file", _test.Http.Requests[1].Body);
    }

    [Fact]
    public async Task Upload_needs_no_intent()
    {
        var path = WriteFile("a.pdf", "a");
        _mcp.ThenText(Link("ut_1"));
        _test.Http.ThenStatus(HttpStatusCode.OK);

        var exit = await _test.RunAsync("files", "upload", path);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(_mcp.Calls[0].Arguments.ContainsKey("intent"));
    }

    [Fact]
    public async Task Upload_of_a_missing_file_is_exit_2_before_any_call()
    {
        var exit = await _test.RunAsync("files", "upload", Path.Combine(_test.Directory, "nope.pdf"));

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_mcp.Calls);
    }

    [Fact]
    public async Task Request_without_a_count_sends_no_arguments_and_prints_the_page()
    {
        _mcp.ThenText("""{"uploadPageUrl":"https://nx-dev.velixo.com/mcp/upload/batch/ub_1","uploadBatchToken":"ub_1","maxFiles":5,"expiresAt":"2026-10-28T16:10:00Z"}""");

        var exit = await _test.RunAsync("files", "request");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(McpToolNames.GetUploadUrl, _mcp.Calls[0].Name);
        Assert.Empty(_mcp.Calls[0].Arguments);
        JsonNode printed = JsonNode.Parse(_test.Console.StdOut)!;
        Assert.Equal("ub_1", (string?)printed["uploadBatchToken"]);
        Assert.Equal("https://nx-dev.velixo.com/mcp/upload/batch/ub_1", (string?)printed["uploadPageUrl"]);
    }

    [Fact]
    public async Task Request_passes_max_files_only_when_given()
    {
        _mcp.ThenText("""{"uploadPageUrl":"u","uploadBatchToken":"ub_1","maxFiles":2,"expiresAt":"x"}""");

        var exit = await _test.RunAsync("files", "request", "--max-files", "2");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(2, (int?)_mcp.Calls[0].Arguments["maxFiles"]);
        Assert.False(_mcp.Calls[0].Arguments.ContainsKey("fileName"));
        Assert.False(_mcp.Calls[0].Arguments.ContainsKey("fileSizeBytes"));
    }

    [Fact]
    public async Task Staged_without_wait_makes_one_call_that_does_not_hold()
    {
        _mcp.ThenText(Staged(submitted: false, files: 1));

        var exit = await _test.RunAsync("files", "staged", "ub_1");

        Assert.Equal(ExitCodes.Success, exit);
        (var tool, JsonObject arguments) = Assert.Single(_mcp.Calls);
        Assert.Equal(McpToolNames.ListStagedFiles, tool);
        Assert.Equal("ub_1", (string?)arguments["uploadBatchToken"]);
        Assert.False((bool)arguments["waitForSubmit"]!);
        Assert.False((bool)JsonNode.Parse(_test.Console.StdOut)!["submitted"]!);
    }

    [Fact]
    public async Task Staged_wait_repeats_the_hold_until_the_user_has_submitted()
    {
        _mcp.ThenText(Staged(submitted: false, files: 0))
            .ThenText(Staged(submitted: false, files: 1))
            .ThenText(Staged(submitted: true, files: 2));

        var exit = await _test.RunAsync("files", "staged", "ub_1", "--wait");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(3, _mcp.Calls.Count);
        Assert.All(_mcp.Calls, call => Assert.True((bool)call.Arguments["waitForSubmit"]!));
        JsonNode printed = JsonNode.Parse(_test.Console.StdOut)!;
        Assert.True((bool)printed["submitted"]!);
        Assert.Equal(2, printed["files"]!.AsArray().Count);
        Assert.Contains("1 of 5 files so far", _test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Staged_wait_stops_on_a_note_and_prints_it_as_a_warning()
    {
        const string note = "Waiting has stopped. ASK THEM whether they still want to send files.";
        _mcp.ThenText(Staged(submitted: false, files: 0))
            .ThenText(Staged(submitted: false, files: 1, note: note))
            .ThenText(Staged(submitted: true, files: 1));

        var exit = await _test.RunAsync("files", "staged", "ub_1", "--wait");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(2, _mcp.Calls.Count);
        Assert.Contains($"warning: {note}", _test.Console.StdErr, StringComparison.Ordinal);
        Assert.Equal(note, (string?)JsonNode.Parse(_test.Console.StdOut)!["note"]);
    }

    [Fact]
    public async Task Staged_with_a_dead_link_is_exit_5_ATTACHMENT_TOKEN_INVALID()
    {
        _mcp.ThenText("ATTACHMENT_TOKEN_INVALID: that upload link is expired or not yours.", isError: true);

        var exit = await _test.RunAsync("files", "staged", "ub_dead", "--wait");

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal("ATTACHMENT_TOKEN_INVALID", (string?)JsonNode.Parse(_test.Console.StdOut)!["error"]!["code"]);
    }

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
