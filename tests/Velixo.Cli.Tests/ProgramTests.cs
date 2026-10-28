using System.Text.Json.Nodes;
using System.CommandLine;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class ProgramTests
{
    [Fact]
    public async Task Help_all_as_json_lists_every_command()
    {
        using var test = new TestServices();

        var exit = await test.RunAsync("help", "--all", "--output", "json");

        Assert.Equal(ExitCodes.Success, exit);
        JsonObject tree = JsonNode.Parse(test.Console.StdOut)!.AsObject();
        var listed = Paths(tree["commands"]!.AsArray()).ToHashSet(StringComparer.Ordinal);
        var expected = Paths(Program.BuildRootCommand(test.Services), "velixo").ToList();

        Assert.NotEmpty(expected);
        Assert.All(expected, path => Assert.Contains(path, listed));
        Assert.Contains("velixo query build", listed);
        Assert.Contains("velixo screen run", listed);
        Assert.Contains("velixo files download", listed);
        Assert.Contains("velixo agent setup", listed);
        Assert.Contains(tree["globalOptions"]!.AsArray(), option => (string?)option!["name"] == "--intent");
    }

    [Fact]
    public async Task Whoami_when_not_signed_in_is_exit_4_with_the_envelope()
    {
        using var test = new TestServices();

        var exit = await test.RunAsync("whoami", "--env", "nx-dev");

        Assert.Equal(ExitCodes.AuthenticationRequired, exit);
        JsonNode envelope = JsonNode.Parse(test.Console.StdOut)!;
        Assert.Equal(ErrorCodes.NotSignedIn, (string?)envelope["error"]!["code"]);
        Assert.Equal(4, (int?)envelope["error"]!["exitCode"]);
        Assert.Equal("velixo login --profile nx-dev", (string?)envelope["error"]!["nextStep"]);
    }

    [Fact]
    public async Task A_tool_call_when_not_signed_in_is_exit_4_before_any_request()
    {
        using var test = new TestServices();

        var exit = await test.RunAsync("connections", "list", "--env", "nx-dev");

        Assert.Equal(ExitCodes.AuthenticationRequired, exit);
        Assert.Equal(ErrorCodes.NotSignedIn, (string?)JsonNode.Parse(test.Console.StdOut)!["error"]!["code"]);
        Assert.Empty(test.Http.Requests);
    }

    [Fact]
    public async Task A_working_tool_without_intent_is_exit_2_before_any_call()
    {
        using var test = new TestServices();
        var mcp = new FakeMcpClient();
        test.Services.McpClientFactory = _ => mcp;

        var exit = await test.RunAsync("call", McpToolNames.ExecuteQuery, "--args", """{"giql":"SELECT 1"}""");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(ErrorCodes.IntentRequired, (string?)JsonNode.Parse(test.Console.StdOut)!["error"]!["code"]);
        Assert.Contains("--intent", (string?)JsonNode.Parse(test.Console.StdOut)!["error"]!["nextStep"], StringComparison.Ordinal);
        Assert.Empty(mcp.Calls);
    }

    [Fact]
    public async Task Call_adds_the_intent_and_unwraps_the_result()
    {
        using var test = new TestServices();
        FakeMcpClient mcp = new FakeMcpClient()
            .ThenText("""{"taskId":"t_1","status":"done","result":{"textualResponse":"{\"rows\":[]}"}}""");
        test.Services.McpClientFactory = _ => mcp;

        var exit = await test.RunAsync("call", McpToolNames.ExecuteQuery, "--args", """{"giql":"SELECT 1"}""", "--intent", "Check the AR balance");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("Check the AR balance", (string?)mcp.Calls[0].Arguments["intent"]);
        Assert.NotNull(JsonNode.Parse(test.Console.StdOut)!["result"]!["data"]!["rows"]);
    }

    [Fact]
    public async Task A_stub_command_is_exit_2_not_implemented()
    {
        using var test = new TestServices();

        var exit = await test.RunAsync("agent", "setup");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(ErrorCodes.NotImplemented, (string?)JsonNode.Parse(test.Console.StdOut)!["error"]!["code"]);
    }

    [Fact]
    public async Task A_bad_flag_is_exit_2_with_the_usage_envelope()
    {
        using var test = new TestServices();

        var exit = await test.RunAsync("connections", "list", "--bogus");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(ErrorCodes.Usage, (string?)JsonNode.Parse(test.Console.StdOut)!["error"]!["code"]);
    }

    [Fact]
    public async Task Config_set_then_get_round_trips()
    {
        using var test = new TestServices();

        Assert.Equal(ExitCodes.Success, await test.RunAsync("config", "set", "profiles.acme.environment", "nx-dev"));
        Assert.Equal(ExitCodes.Success, await test.RunAsync("config", "set", "defaultProfile", "acme"));
        test.Console.OutWriter.GetStringBuilder().Clear();

        Assert.Equal(ExitCodes.Success, await test.RunAsync("config", "get", "profiles.acme.environment"));
        Assert.Equal("nx-dev", (string?)JsonNode.Parse(test.Console.StdOut)!["value"]);
        Assert.Equal("acme", test.Services.Settings.ProfileName);
        Assert.Equal(new Uri("https://nx-dev.velixo.com/mcp"), test.Services.Settings.McpUrl);
    }

    private static IEnumerable<string> Paths(JsonArray commands) =>
        commands.SelectMany(command => new[] { (string)command!["path"]! }
            .Concat(command["commands"] is JsonArray children ? Paths(children) : []));

    private static IEnumerable<string> Paths(Command command, string path) =>
        command.Subcommands.SelectMany(child => new[] { $"{path} {child.Name}" }.Concat(Paths(child, $"{path} {child.Name}")));
}
