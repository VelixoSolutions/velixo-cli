using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class NotifyCommandTests : IDisposable
{
    private const string Delivered = """{"accepted":true,"reachedNobody":false,"recipients":[{"name":"Ana Diaz","channels":["email","portal"]}],"note":"Delivered on at least one channel that reaches out."}""";

    private const string QueryResult = """{"columns":[{"caption":"Customer"},{"caption":"Balance"}],"rows":[["Acme",1200.5],["Globex",300]],"totalCount":2,"truncated":false}""";

    private readonly TestServices _test = new();
    private readonly FakeMcpClient _mcp = new();

    public NotifyCommandTests()
    {
        _test.Services.McpClientFactory = _ => _mcp;
    }

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task Notify_sends_the_intent_the_text_and_the_default_source_name()
    {
        _mcp.ThenText(Delivered);

        var exit = await _test.RunAsync("notify", "--subject", "AR is ready", "--body", "The aging is done.", "--intent", "Tell Ana the aging is ready");

        Assert.Equal(ExitCodes.Success, exit);
        (var tool, JsonObject arguments) = Assert.Single(_mcp.Calls);
        Assert.Equal(McpToolNames.SendNotification, tool);
        Assert.Equal("Tell Ana the aging is ready", (string?)arguments["intent"]);
        Assert.Equal("AR is ready", (string?)arguments["subject"]);
        Assert.Equal("The aging is done.", (string?)arguments["body"]);
        Assert.Equal("velixo-cli", (string?)arguments["sourceName"]);
        Assert.Equal(["intent", "subject", "body", "sourceName"], arguments.Select(argument => argument.Key));

        JsonNode printed = JsonNode.Parse(_test.Console.StdOut)!;
        Assert.True((bool)printed["accepted"]!);
        Assert.False((bool)printed["reachedNobody"]!);
        Assert.DoesNotContain("warning:", _test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notify_passes_every_option_through()
    {
        _mcp.ThenText(Delivered);

        var exit = await _test.RunAsync(
            "notify",
            "--subject", "Close blocked",
            "--body", "Two batches are on hold.",
            "--target", "ana@acme.com",
            "--link", "https://erp.acme.com/batches",
            "--link-label", "Open in Acumatica",
            "--severity", "Warning",
            "--source-name", "Month-end close",
            "--intent", "Warn Ana");

        Assert.Equal(ExitCodes.Success, exit);
        JsonObject arguments = _mcp.Calls[0].Arguments;
        Assert.Equal("ana@acme.com", (string?)arguments["target"]);
        Assert.Equal("https://erp.acme.com/batches", (string?)arguments["link"]);
        Assert.Equal("Open in Acumatica", (string?)arguments["linkLabel"]);
        Assert.Equal("Warning", (string?)arguments["severity"]);
        Assert.Equal("Month-end close", (string?)arguments["sourceName"]);
        Assert.False(arguments.ContainsKey("payload"));
    }

    [Fact]
    public async Task Payload_is_the_result_data_of_a_saved_query_run()
    {
        var saved = new JsonObject
        {
            ["taskId"] = "t_1",
            ["status"] = "done",
            ["result"] = new JsonObject { ["data"] = JsonNode.Parse(QueryResult), ["attachments"] = new JsonArray() },
        };
        var file = WriteFile("run.json", saved.ToJsonString());
        _mcp.ThenText(Delivered);

        var exit = await _test.RunAsync("notify", "--subject", "s", "--body", "b", "--payload", file, "--intent", "Send the balances");

        Assert.Equal(ExitCodes.Success, exit);
        var payload = (string?)_mcp.Calls[0].Arguments["payload"];
        Assert.Equal(JsonNode.Parse(QueryResult)!.ToJsonString(), JsonNode.Parse(payload!)!.ToJsonString());
        Assert.DoesNotContain("taskId", payload, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', payload!);
    }

    [Fact]
    public async Task Payload_is_the_whole_file_when_it_is_a_query_result_itself()
    {
        var file = WriteFile("data.json", QueryResult);
        _mcp.ThenText(Delivered);

        var exit = await _test.RunAsync("notify", "--subject", "s", "--body", "b", "--payload", file, "--intent", "Send the balances");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            JsonNode.Parse(((string?)_mcp.Calls[0].Arguments["payload"])!)!.ToJsonString(),
            JsonNode.Parse(QueryResult)!.ToJsonString());
    }

    [Theory]
    [InlineData("""{"hello":"world"}""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("not json")]
    public async Task A_payload_that_is_not_a_query_run_output_is_exit_2_before_any_call(string content)
    {
        var file = WriteFile("other.json", content);

        var exit = await _test.RunAsync("notify", "--subject", "s", "--body", "b", "--payload", file, "--intent", "Send the balances");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_mcp.Calls);
    }

    [Fact]
    public async Task A_missing_payload_file_is_exit_2()
    {
        var exit = await _test.RunAsync("notify", "--subject", "s", "--body", "b", "--payload", Path.Combine(_test.Directory, "none.json"), "--intent", "x");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_mcp.Calls);
    }

    [Fact]
    public async Task Reaching_nobody_prints_a_warning_and_still_exits_0()
    {
        _mcp.ThenText("""{"accepted":true,"reachedNobody":true,"recipients":[{"name":"Ana Diaz","channels":["portal"]}],"note":"Recorded, but nothing pushed it to them."}""");

        var exit = await _test.RunAsync("notify", "--subject", "s", "--body", "b", "--intent", "Tell Ana");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True((bool)JsonNode.Parse(_test.Console.StdOut)!["reachedNobody"]!);
        Assert.Contains("warning: The notification is recorded, but nothing pushed it to anyone", _test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("Do not report the recipient as notified", _test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_target_nobody_matches_is_exit_5_with_the_servers_text()
    {
        _mcp.ThenText("That target could not be reached: it matched nobody you are allowed to notify.", isError: true);

        var exit = await _test.RunAsync("notify", "--subject", "s", "--body", "b", "--target", "who@else.com", "--intent", "Tell them");

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Contains("matched nobody", (string?)JsonNode.Parse(_test.Console.StdOut)!["error"]!["message"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_severity_is_exit_2_before_any_call()
    {
        var exit = await _test.RunAsync("notify", "--subject", "s", "--body", "b", "--severity", "Loud", "--intent", "x");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_mcp.Calls);
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_test.Directory, name);
        File.WriteAllText(path, content);
        return path;
    }
}
