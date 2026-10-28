using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class ConnectionsCommandsTests : IDisposable
{
    private const string Connections = """[{"name":"gab-dev","type":"Acumatica","status":"ok"},{"name":"skyline","type":"SageIntacct","status":"ok"}]""";

    private const string QueryDone = """{"taskId":"t_1","status":"done","result":{"textualResponse":"{\"columns\":[],\"rows\":[],\"totalCount\":0}"}}""";

    private readonly TestServices _test = new();
    private readonly FakeMcpClient _mcp = new();

    public ConnectionsCommandsTests()
    {
        _test.Services.McpClientFactory = _ => _mcp;
    }

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task Use_writes_the_connection_of_the_current_profile_and_clear_removes_it()
    {
        var exit = await _test.RunAsync("connections", "use", "gab-dev", "--profile", "acme");

        Assert.Equal(ExitCodes.Success, exit);
        JsonNode printed = JsonNode.Parse(_test.Console.StdOut)!;
        Assert.Equal("acme", (string?)printed["profile"]);
        Assert.Equal("gab-dev", (string?)printed["connection"]);
        Assert.Equal("gab-dev", _test.Services.Profiles.LoadConfig().Profiles["acme"].Connection);
        Assert.Empty(_mcp.Calls);

        exit = await _test.RunAsync("connections", "use", "--clear", "--profile", "acme");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Null(_test.Services.Profiles.LoadConfig().Profiles["acme"].Connection);
    }

    [Theory]
    [InlineData]
    [InlineData("gab-dev", "--clear")]
    public async Task Use_needs_either_a_name_or_clear(params string[] flags)
    {
        var exit = await _test.RunAsync(["connections", "use", .. flags]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_test.Services.Profiles.LoadConfig().Profiles);
    }

    [Fact]
    public async Task A_command_uses_the_default_and_the_connection_flag_overrides_it()
    {
        await _test.RunAsync("connections", "use", "gab-dev");
        var query = Path.Combine(_test.Directory, "q.vql");
        File.WriteAllText(query, "FROM X SELECT Y");
        _mcp.ThenText(QueryDone).ThenText(QueryDone);

        await _test.RunAsync("query", "run", "--query", query);
        await _test.RunAsync("query", "run", "--query", query, "--connection", "skyline");

        Assert.Equal("gab-dev", (string?)_mcp.Calls[0].Arguments["connectionName"]);
        Assert.Equal("skyline", (string?)_mcp.Calls[1].Arguments["connectionName"]);
    }

    [Fact]
    public async Task List_marks_the_default_connection_in_json()
    {
        await _test.RunAsync("connections", "use", "gab-dev");
        _test.Console.OutWriter.GetStringBuilder().Clear();
        _mcp.ThenText(Connections);

        var exit = await _test.RunAsync("connections", "list");

        Assert.Equal(ExitCodes.Success, exit);
        JsonArray listed = JsonNode.Parse(_test.Console.StdOut)!.AsArray();
        Assert.True((bool?)listed[0]!["default"]);
        Assert.False(listed[1]!.AsObject().ContainsKey("default"));
    }

    [Fact]
    public async Task List_marks_the_default_connection_in_a_table()
    {
        await _test.RunAsync("connections", "use", "gab-dev");
        _test.Console.OutWriter.GetStringBuilder().Clear();
        _mcp.ThenText(Connections);

        var exit = await _test.RunAsync("connections", "list", "--output", "table");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("gab-dev (default)", _test.Console.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("skyline (default)", _test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_because_several_connections_fit_names_connections_use()
    {
        const string refusal =
            "Several ERP connections are usable, so this call needs an explicit connectionName. Call list_connections and ask the user which one to use — do not guess.";

        CliException error = Assert.Throws<CliException>(() => ToolResultUnwrapper.Unwrap(McpToolResult.FromText(refusal, isError: true)));

        Assert.Equal(ExitCodes.ServerRefused, error.ExitCode);
        Assert.Equal(ErrorCodes.ToolError, error.Code);
        Assert.Contains("velixo connections use NAME", error.NextStep, StringComparison.Ordinal);
    }
}
