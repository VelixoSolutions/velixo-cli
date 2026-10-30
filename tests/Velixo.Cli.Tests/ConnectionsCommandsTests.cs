using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class ConnectionsCommandsTests : IDisposable
{
    private const string Connections = """[{"name":"gab-dev","type":"Acumatica","status":"ok"},{"name":"skyline","type":"SageIntacct","status":"ok"}]""";

    private const string Listed = """
        [{"name":"demo","type":"acumatica","status":"ok","description":"acumatica ERP connection","url":"https://demo.velixo.com","company":"Company"},
         {"name":"skyline","type":"sage_intacct","status":"ok","description":"sage_intacct ERP connection","url":null,"company":"Skyline"}]
        """;

    private const string QueryDone = """{"taskId":"t_1","status":"done","result":{"textualResponse":"{\"columns\":[],\"rows\":[],\"totalCount\":0}"}}""";

    private readonly TestServices _test = new();
    private readonly FakeMcpClient _mcp = new();

    public ConnectionsCommandsTests()
    {
        _test.Services.McpClientFactory = _ => _mcp;
    }

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task List_prints_each_connections_company_and_address_as_the_server_gives_them()
    {
        _mcp.ThenText(Listed);

        var exit = await _test.RunAsync("connections", "list");

        Assert.Equal(ExitCodes.Success, exit);
        JsonArray printed = JsonNode.Parse(_test.Console.StdOut)!.AsArray();
        Assert.Equal("https://demo.velixo.com", (string?)printed[0]!["url"]);
        Assert.Equal("Company", (string?)printed[0]!["company"]);
        Assert.Null(printed[1]!["url"]);
        Assert.Equal("Skyline", (string?)printed[1]!["company"]);
    }

    [Fact]
    public async Task List_as_a_table_ends_with_the_address_and_leaves_out_the_description()
    {
        _mcp.ThenText(Listed);

        var exit = await _test.RunAsync("connections", "list", "--output", "table");

        Assert.Equal(ExitCodes.Success, exit);
        var header = _test.Console.StdOut.Split('\n')[0];
        Assert.DoesNotContain("description", header, StringComparison.Ordinal);
        Assert.True(header.IndexOf("company", StringComparison.Ordinal) < header.IndexOf("url", StringComparison.Ordinal), header);
        Assert.EndsWith("url", header.TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("https://demo.velixo.com", _test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_as_a_table_keeps_the_description_an_older_server_sends()
    {
        _mcp.ThenText("""[{"name":"gab-dev","type":"acumatica","status":"ok","description":"Acumatica 2025 R1, tenant Company"}]""");

        var exit = await _test.RunAsync("connections", "list", "--output", "table");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Acumatica 2025 R1, tenant Company", _test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_as_csv_ends_with_the_address_even_when_a_later_row_has_more_fields()
    {
        _mcp.ThenText("""
            [{"name":"demo","type":"acumatica","status":"ok","description":"acumatica ERP connection","url":"https://demo.velixo.com","company":"Company"},
             {"name":"other","type":"acumatica","status":"ok","url":"https://other.velixo.com","company":"Other","region":"eu"}]
            """);

        var exit = await _test.RunAsync("connections", "list", "--output", "csv");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("name,type,status,company,region,url", _test.Console.StdOut.Split('\n')[0].TrimEnd('\r'));
    }

    [Fact]
    public async Task Switch_with_a_name_writes_the_server_spelling_and_clear_removes_it()
    {
        _mcp.ThenText(Connections);

        var exit = await _test.RunAsync("connections", "switch", "GAB-DEV", "--profile", "acme");

        Assert.Equal(ExitCodes.Success, exit);
        JsonNode printed = JsonNode.Parse(_test.Console.StdOut)!;
        Assert.Equal("acme", (string?)printed["profile"]);
        Assert.Equal("gab-dev", (string?)printed["connection"]);
        Assert.Equal("gab-dev", _test.Services.Profiles.LoadConfig().Profiles["acme"].Connection);

        exit = await _test.RunAsync("connections", "switch", "--clear", "--profile", "acme");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Null(_test.Services.Profiles.LoadConfig().Profiles["acme"].Connection);
        Assert.Single(_mcp.Calls);
    }

    [Fact]
    public async Task Switch_refuses_a_name_the_server_does_not_list()
    {
        _mcp.ThenText(Connections);

        var exit = await _test.RunAsync("connections", "switch", "nope");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_test.Services.Profiles.LoadConfig().Profiles);
    }

    [Fact]
    public async Task Switch_refuses_a_name_and_clear_together()
    {
        var exit = await _test.RunAsync("connections", "switch", "gab-dev", "--clear");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_mcp.Calls);
    }

    [Fact]
    public async Task Switch_without_a_name_and_without_a_terminal_asks_for_the_name()
    {
        _mcp.ThenText(Connections);

        var exit = await _test.RunAsync("connections", "switch");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains(ConnectionsCommands.SwitchUsage, _test.Console.StdOut, StringComparison.Ordinal);
        Assert.Empty(_test.Services.Profiles.LoadConfig().Profiles);
    }

    [Fact]
    public async Task Switch_on_a_terminal_starts_on_the_current_default_and_picks_with_the_arrows()
    {
        using var test = new TestServices(redirected: false);
        var mcp = new FakeMcpClient();
        test.Services.McpClientFactory = _ => mcp;
        mcp.ThenText(Connections).ThenText(Connections);
        await test.RunAsync("connections", "switch", "skyline");
        test.Console.Keys.Enqueue(Key(ConsoleKey.UpArrow));
        test.Console.Keys.Enqueue(Key(ConsoleKey.Enter));

        var exit = await test.RunAsync("connections", "switch");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("gab-dev", test.Services.Profiles.LoadConfig().Profiles["prod"].Connection);
        Assert.Contains("skyline", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("(current)", test.Console.StdErr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConsoleKey.Escape, ConsoleModifiers.None)]
    [InlineData(ConsoleKey.Q, ConsoleModifiers.None)]
    [InlineData(ConsoleKey.C, ConsoleModifiers.Control)]
    public async Task Cancelling_the_picker_changes_nothing(ConsoleKey key, ConsoleModifiers modifiers)
    {
        using var test = new TestServices(redirected: false);
        var mcp = new FakeMcpClient();
        test.Services.McpClientFactory = _ => mcp;
        mcp.ThenText(Connections);
        test.Console.Keys.Enqueue(Key(ConsoleKey.DownArrow));
        test.Console.Keys.Enqueue(Key(key, modifiers));

        var exit = await test.RunAsync("connections", "switch");

        Assert.Equal(ExitCodes.Interrupted, exit);
        Assert.Empty(test.Services.Profiles.LoadConfig().Profiles);
        Assert.True(test.Console.StdErr.LastIndexOf("\u001b[?25h", StringComparison.Ordinal) > test.Console.StdErr.LastIndexOf("\u001b[?25l", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_picker_wraps_around_and_scrolls_a_long_list()
    {
        using var test = new TestServices(redirected: false);
        var rows = Enumerable.Range(1, Picker.VisibleRows + 5).Select(index => $"c{index:00}").ToList();
        test.Console.Keys.Enqueue(Key(ConsoleKey.UpArrow));
        test.Console.Keys.Enqueue(Key(ConsoleKey.Enter));

        var picked = Picker.Choose(test.Console, "Pick", rows, 0, useColor: false);

        Assert.Equal(rows.Count - 1, picked);
        Assert.Contains($"❯ c{rows.Count:00}", test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_command_uses_the_default_and_the_connection_flag_overrides_it()
    {
        _mcp.ThenText(Connections);
        await _test.RunAsync("connections", "switch", "gab-dev");
        var query = Path.Combine(_test.Directory, "q.vql");
        File.WriteAllText(query, "FROM X SELECT Y");
        _mcp.ThenText(QueryDone).ThenText(QueryDone);

        await _test.RunAsync("query", "run", "--query", query);
        await _test.RunAsync("query", "run", "--query", query, "--connection", "skyline");

        Assert.Equal("gab-dev", (string?)_mcp.Calls[1].Arguments["connectionName"]);
        Assert.Equal("skyline", (string?)_mcp.Calls[2].Arguments["connectionName"]);
    }

    [Fact]
    public async Task List_marks_the_default_connection_in_json()
    {
        _mcp.ThenText(Connections);
        await _test.RunAsync("connections", "switch", "gab-dev");
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
        _mcp.ThenText(Connections);
        await _test.RunAsync("connections", "switch", "gab-dev");
        _test.Console.OutWriter.GetStringBuilder().Clear();
        _mcp.ThenText(Connections);

        var exit = await _test.RunAsync("connections", "list", "--output", "table");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("gab-dev (default)", _test.Console.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("skyline (default)", _test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_because_several_connections_fit_names_connections_switch()
    {
        const string refusal =
            "Several ERP connections are usable, so this call needs an explicit connectionName. Call list_connections and ask the user which one to use — do not guess.";

        CliException error = Assert.Throws<CliException>(() => ToolResultUnwrapper.Unwrap(McpToolResult.FromText(refusal, isError: true)));

        Assert.Equal(ExitCodes.ServerRefused, error.ExitCode);
        Assert.Equal(ErrorCodes.ToolError, error.Code);
        Assert.Contains("velixo connections switch NAME", error.NextStep, StringComparison.Ordinal);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, ConsoleModifiers modifiers = ConsoleModifiers.None) =>
        new('\0', key, shift: false, alt: false, control: modifiers.HasFlag(ConsoleModifiers.Control));
}
