using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class TaskCommandsTests : IDisposable
{
    private const string Running = """{"taskId":"t_7","status":"running","progress":{"label":"Release on SO301000","elapsedMs":134000,"stalledForMs":48000}}""";
    private const string Done = """{"taskId":"t_7","status":"done","result":{"textualResponse":"{\"queries\":[{\"purpose\":\"P\",\"giql\":\"FROM A SELECT B\"}],\"note\":\"n\"}"}}""";

    private readonly TestServices _test = new();
    private readonly FakeMcpClient _mcp = new();

    public TaskCommandsTests() => _test.Services.McpClientFactory = _ => _mcp;

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task Status_polls_once_and_prints_the_running_envelope_without_an_intent()
    {
        _mcp.ThenText(Running);

        var exit = await _test.RunAsync("task", "status", "t_7");

        Assert.Equal(ExitCodes.Success, exit);
        (string name, JsonObject arguments) = Assert.Single(_mcp.Calls);
        Assert.Equal(McpToolNames.GetStatus, name);
        Assert.Equal("""{"taskId":"t_7"}""", arguments.ToJsonString());
        Assert.Equal("running", (string?)Output()["status"]);
    }

    [Fact]
    public async Task Status_with_wait_follows_until_done_with_progress_on_stderr()
    {
        _mcp.ThenText(Running).ThenText(Running).ThenText(Done);

        var exit = await _test.RunAsync("task", "status", "t_7", "--wait");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(3, _mcp.Calls.Count);
        Assert.All(_mcp.Calls, call => Assert.Equal(McpToolNames.GetStatus, call.Name));
        Assert.Contains("Release on SO301000, 2m14s, no change for 48s", _test.Console.StdErr, StringComparison.Ordinal);
        Assert.Equal("FROM A SELECT B", (string?)Output()["result"]!["data"]!["queries"]![0]!["query"]);
    }

    [Theory]
    [InlineData("""{"taskId":"t_7","status":"failed","error":"Acumatica answered HTTP 500."}""", true, ErrorCodes.TaskFailed)]
    [InlineData("""{"taskId":"t_7","status":"expired","message":"Task not found."}""", false, ErrorCodes.TaskExpired)]
    public async Task A_failed_or_expired_task_is_exit_6_with_the_servers_text(string envelope, bool isError, string code)
    {
        _mcp.ThenText(Running).ThenText(envelope, isError);

        var exit = await _test.RunAsync("task", "status", "t_7", "--wait");

        Assert.Equal(ExitCodes.TaskFailed, exit);
        JsonNode error = Output()["error"]!;
        Assert.Equal(code, (string?)error["code"]);
        Assert.Equal("t_7", (string?)error["taskId"]);
    }

    private JsonNode Output() => JsonNode.Parse(_test.Console.StdOut)!;
}
