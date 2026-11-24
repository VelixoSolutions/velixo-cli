using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class FeedbackCommandTests : IDisposable
{
    private const string Received = "Thank you. Velixo R&D received the report (0f1e2d3c). Do not submit it again.";

    private readonly TestServices _test = new();
    private readonly FakeMcpClient _mcp = new();

    public FeedbackCommandTests()
    {
        _test.Services.McpClientFactory = _ => _mcp;
    }

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task Feedback_sends_every_argument_under_the_servers_names_and_no_intent()
    {
        _mcp.ThenText(Received);

        var exit = await _test.RunAsync(
            "feedback",
            "--tool", "query run",
            "--category", "missing-filter",
            "--severity", "worked-around",
            "--attempted", "The user wanted open AR invoices by branch.",
            "--problem", "There is no way to filter a saved inquiry by branch.",
            "--proposed-change", "Add a --filter flag to query run --inquiry.",
            "--workaround", "Built a new query instead.");

        Assert.Equal(ExitCodes.Success, exit);
        (var tool, JsonObject arguments) = Assert.Single(_mcp.Calls);
        Assert.Equal(McpToolNames.SubmitFeedback, tool);
        Assert.Equal(
            ["tool", "category", "severity", "attempted", "problem", "proposedChange", "workaround"],
            arguments.Select(argument => argument.Key));
        Assert.Equal(McpToolNames.ExecuteQuery, (string?)arguments["tool"]);
        Assert.Equal("missing-filter", (string?)arguments["category"]);
        Assert.Equal("worked-around", (string?)arguments["severity"]);
        Assert.Equal("Add a --filter flag to query run --inquiry.", (string?)arguments["proposedChange"]);
        Assert.Equal("Built a new query instead.", (string?)arguments["workaround"]);

        JsonNode printed = JsonNode.Parse(_test.Console.StdOut)!;
        Assert.Equal(Received, (string?)printed["result"]!["text"]);
    }

    [Fact]
    public async Task Feedback_without_a_workaround_leaves_it_out()
    {
        _mcp.ThenText(Received);

        var exit = await _test.RunAsync(Feedback("none"));

        Assert.Equal(ExitCodes.Success, exit);
        JsonObject arguments = _mcp.Calls[0].Arguments;
        Assert.False(arguments.ContainsKey("workaround"));
        Assert.Equal("none", (string?)arguments["tool"]);
    }

    [Theory]
    [InlineData("query run", "execute_query")]
    [InlineData("velixo  command   build", "prepare_commands")]
    [InlineData("notify", "send_notification")]
    [InlineData("execute_import_scenario", "execute_import_scenario")]
    [InlineData("none", "none")]
    [InlineData("files stage", "files stage")]
    public void A_command_names_the_tool_it_calls(string value, string expected)
    {
        Assert.Equal(expected, FeedbackCommand.ToToolName(value));
    }

    [Theory]
    [InlineData("--category", "broken")]
    [InlineData("--severity", "urgent")]
    public async Task A_value_outside_the_servers_list_is_exit_2_before_any_call(string option, string value)
    {
        var arguments = Feedback("query run").ToList();
        arguments[arguments.IndexOf(option) + 1] = value;

        var exit = await _test.RunAsync([.. arguments]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_mcp.Calls);
    }

    [Fact]
    public async Task An_empty_text_is_exit_2_before_any_call()
    {
        var arguments = Feedback("query run").ToList();
        arguments[arguments.IndexOf("--problem") + 1] = "  ";

        var exit = await _test.RunAsync([.. arguments]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_mcp.Calls);
    }

    [Fact]
    public async Task A_report_the_server_refuses_is_exit_5_with_its_text()
    {
        _mcp.ThenText("attempted, problem, proposedChange and workaround may hold at most 4096 characters each.", isError: true);

        var exit = await _test.RunAsync(Feedback("query run"));

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Contains("at most 4096 characters", (string?)JsonNode.Parse(_test.Console.StdOut)!["error"]!["message"], StringComparison.Ordinal);
    }

    private static string[] Feedback(string tool) =>
    [
        "feedback",
        "--tool", tool,
        "--category", "missing-capability",
        "--severity", "blocked",
        "--attempted", "The user wanted to void a check.",
        "--problem", "No command voids a check.",
        "--proposed-change", "Let command build target the void action.",
    ];
}
