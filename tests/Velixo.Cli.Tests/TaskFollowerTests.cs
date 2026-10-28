using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class TaskFollowerTests
{
    private const string Running = """{"taskId":"t_1","status":"running","progress":{"label":"Release on SO301000","elapsedMs":134000,"stalledForMs":48000}}""";
    private const string Done = """{"taskId":"t_1","status":"done","result":{"textualResponse":"{\"ok\":true}"}}""";

    private readonly TestConsole _console = new();

    [Fact]
    public async Task Polls_get_status_until_done_and_reports_progress()
    {
        FakeMcpClient mcp = new FakeMcpClient().ThenText(Running).ThenText(Running).ThenText(Done);

        UnwrappedToolResult result = await Follower(mcp).RunAsync(
            token => mcp.CallToolAsync(McpToolNames.PrepareQuery, [], token),
            new TaskFollowOptions(),
            TestContext.Current.CancellationToken);

        Assert.Equal("done", result.Status);
        Assert.True((bool)result.Data!["ok"]!);
        Assert.Equal([McpToolNames.PrepareQuery, McpToolNames.GetStatus, McpToolNames.GetStatus], mcp.Calls.Select(call => call.Name));
        Assert.Equal("t_1", (string?)mcp.Calls[1].Arguments["taskId"]);
        Assert.Contains("Release on SO301000, 2m14s, no change for 48s", _console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_wait_returns_the_running_envelope()
    {
        FakeMcpClient mcp = new FakeMcpClient().ThenText(Running);

        UnwrappedToolResult result = await Follower(mcp).RunAsync(
            token => mcp.CallToolAsync(McpToolNames.PrepareQuery, [], token),
            new TaskFollowOptions { NoWait = true },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsRunning);
        Assert.Single(mcp.Calls);
    }

    [Fact]
    public async Task Timeout_while_following_is_exit_124_with_the_task_id()
    {
        FakeMcpClient mcp = new FakeMcpClient().ThenText(Running).ThenHang();

        CliException error = await Assert.ThrowsAsync<CliException>(() => Follower(mcp).RunAsync(
            token => mcp.CallToolAsync(McpToolNames.PrepareQuery, [], token),
            new TaskFollowOptions { Timeout = TimeSpan.FromMilliseconds(200) },
            TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Timeout, error.ExitCode);
        Assert.Equal("t_1", error.TaskId);
        Assert.Equal("velixo task status t_1 --wait", error.NextStep);
    }

    [Fact]
    public async Task A_failure_while_following_is_exit_6()
    {
        FakeMcpClient mcp = new FakeMcpClient()
            .ThenText(Running)
            .ThenText("""{"taskId":"t_1","status":"failed","error":"The Release button is disabled."}""", isError: true);

        CliException error = await Assert.ThrowsAsync<CliException>(() => Follower(mcp).FollowAsync(
            ToolResultUnwrapper.Unwrap(McpToolResult.FromText(Running)),
            new TaskFollowOptions(),
            TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.TaskFailed, error.ExitCode);
    }

    [Fact]
    public void Progress_line_leaves_out_a_short_stall()
    {
        var line = TaskFollower.DescribeProgress("t_1", JsonNode.Parse("""{"label":"Building query","elapsedMs":12000,"stalledForMs":2000}""")!.AsObject());

        Assert.Equal("Building query, 12s", line);
    }

    private TaskFollower Follower(FakeMcpClient mcp) =>
        new(mcp, new OutputWriter(_console, new OutputSettings()), TimeProvider.System);
}
