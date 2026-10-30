using System.Net;
using System.Text.Json.Nodes;
using Velixo.Cli.Auth;
using Velixo.Cli.Config;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class ImportCommandsTests : IDisposable
{
    private const string Intent = "Import the new customers the user sent";

    private static readonly string[] _baseArguments =
    [
        "import", "run", "--connection", "gab-dev", "--scenario", "Import Customers", "--intent", Intent,
    ];

    private readonly TestServices _test = new();
    private readonly FakeMcpClient _mcp = new();

    public ImportCommandsTests()
    {
        _test.Services.McpClientFactory = _ => _mcp;
    }

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task Scenarios_without_a_connection_is_exit_2_before_any_call()
    {
        var exit = await _test.RunAsync("import", "scenarios");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(ErrorCodes.Usage, (string?)JsonNode.Parse(_test.Console.StdOut)!["error"]!["code"]);
        Assert.Empty(_mcp.Calls);
    }

    [Fact]
    public async Task Scenarios_prints_the_normalised_array_and_passes_the_filters()
    {
        _mcp.ThenText(Listing);

        var exit = await _test.RunAsync("import", "scenarios", "--connection", "gab-dev", "--screen", "AR303000", "--filter", "customer");

        Assert.Equal(ExitCodes.Success, exit);
        (var tool, JsonObject arguments) = Assert.Single(_mcp.Calls);
        Assert.Equal(McpToolNames.ListImportScenarios, tool);
        Assert.Equal("gab-dev", (string?)arguments["connectionName"]);
        Assert.Equal("AR303000", (string?)arguments["screenId"]);
        Assert.Equal("customer", (string?)arguments["nameFilter"]);
        Assert.False(arguments.ContainsKey("intent"));

        JsonArray scenarios = JsonNode.Parse(_test.Console.StdOut)!.AsArray();
        Assert.Equal(2, scenarios.Count);
        Assert.Equal("Import Customers", (string?)scenarios[0]!["name"]);
        Assert.Equal("AR303000", (string?)scenarios[0]!["screenId"]);
        Assert.True((bool)scenarios[0]!["apiEnabled"]!);
        Assert.Equal(["Customer ID", "Customer Name"], scenarios[0]!["columns"]!.AsArray().Select(column => (string?)column));
        Assert.False((bool)scenarios[1]!["apiEnabled"]!);
        Assert.False(scenarios[1]!.AsObject().ContainsKey("columns"));
    }

    [Fact]
    public async Task Scenarios_on_a_terminal_is_a_table_with_the_columns_joined_by_comma()
    {
        using var terminal = new TestServices(redirected: false);
        FakeMcpClient mcp = new FakeMcpClient().ThenText(Listing);
        terminal.Services.McpClientFactory = _ => mcp;

        var exit = await terminal.RunAsync("import", "scenarios", "--connection", "gab-dev");

        Assert.Equal(ExitCodes.Success, exit);
        var lines = terminal.Console.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["name", "screen", "apiEnabled", "columns"], lines[0].Split("  ", StringSplitOptions.RemoveEmptyEntries).Select(cell => cell.Trim()));
        Assert.Contains(lines, line => line.Contains("Import Customers", StringComparison.Ordinal)
            && line.Contains("AR303000", StringComparison.Ordinal)
            && line.Contains("true", StringComparison.Ordinal)
            && line.Contains("Customer ID, Customer Name", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Locked Scenario", StringComparison.Ordinal) && line.Contains("false", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_validates_then_runs_on_the_same_token_and_prints_the_report()
    {
        var file = WriteCsv();
        QueueStaging();
        _mcp.ThenText(Preview(rows: 15, warnings: ["Delimiter ';' was guessed from the first line."]))
            .ThenText(Done(Report(total: 15, processed: 15, failed: 0)));

        var exit = await _test.RunAsync([.. _baseArguments, "--file", file, "--sheet", "Customers", "--range", "A1:F16", "--yes"]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            [McpToolNames.GetUploadUrl, McpToolNames.ExecuteImportScenario, McpToolNames.ExecuteImportScenario],
            _mcp.Calls.Select(call => call.Name));

        JsonObject preview = _mcp.Calls[1].Arguments;
        JsonObject run = _mcp.Calls[2].Arguments;
        Assert.True((bool)preview["validateOnly"]!);
        Assert.False(run.ContainsKey("validateOnly"));
        foreach (JsonObject arguments in new[] { preview, run })
        {
            Assert.Equal(Intent, (string?)arguments["intent"]);
            Assert.Equal("gab-dev", (string?)arguments["connectionName"]);
            Assert.Equal("Import Customers", (string?)arguments["scenarioName"]);
            Assert.Equal("customers.csv", (string?)arguments["fileName"]);
            Assert.Equal("ut_import", (string?)arguments["fileToken"]);
            Assert.Equal("Customers", (string?)arguments["sheetName"]);
            Assert.Equal("A1:F16", (string?)arguments["range"]);
            Assert.False(arguments.ContainsKey("breakOnError"));
        }

        JsonNode printed = JsonNode.Parse(_test.Console.StdOut)!;
        Assert.Equal("t_7", (string?)printed["taskId"]);
        Assert.Equal("done", (string?)printed["status"]);
        Assert.Equal(15, (int?)printed["result"]!["data"]!["processed"]);

        var errors = _test.Console.StdErr;
        Assert.Contains("scenario:   Import Customers", errors, StringComparison.Ordinal);
        Assert.Contains("AR303000 (Customers)", errors, StringComparison.Ordinal);
        Assert.Contains("gab-dev", errors, StringComparison.Ordinal);
        Assert.Contains("15 rows", errors, StringComparison.Ordinal);
        Assert.Contains("warning: Delimiter ';' was guessed from the first line.", errors, StringComparison.Ordinal);
        Assert.Contains("not atomic", errors, StringComparison.Ordinal);
        Assert.DoesNotContain("rows failed", errors, StringComparison.Ordinal);
        Assert.Equal(1, errors.Split("Delimiter ';' was guessed", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task Run_passes_break_on_error_to_both_calls()
    {
        var file = WriteCsv();
        QueueStaging();
        _mcp.ThenText(Preview(rows: 3, warnings: [])).ThenText(Done(Report(total: 3, processed: 3, failed: 0)));

        var exit = await _test.RunAsync([.. _baseArguments, "--file", file, "--break-on-error", "--yes"]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True((bool)_mcp.Calls[1].Arguments["breakOnError"]!);
        Assert.True((bool)_mcp.Calls[2].Arguments["breakOnError"]!);
    }

    [Fact]
    public async Task A_file_token_imports_a_staged_file_without_staging_it_again()
    {
        _test.Services.Profiles.UpdateStagedFiles("prod", staged =>
            staged.Files = [new StoredStagedFile { FileToken = "ft_9", FileName = "customers.csv", SizeBytes = 120, ExpiresAt = "2999-01-01T00:00:00Z" }]);
        _mcp.ThenText(Preview(rows: 3, warnings: []));

        var exit = await _test.RunAsync([.. _baseArguments, "--file-token", "ft_9", "--validate-only"]);

        Assert.Equal(ExitCodes.Success, exit);
        JsonObject sent = Assert.Single(_mcp.Calls).Arguments;
        Assert.Equal("ft_9", (string?)sent["fileToken"]);
        Assert.Equal("customers.csv", (string?)sent["fileName"]);
    }

    [Fact]
    public async Task An_import_needs_a_file_or_a_file_token_and_not_both()
    {
        var file = WriteCsv();

        Assert.Equal(ExitCodes.Usage, await _test.RunAsync([.. _baseArguments, "--validate-only"]));
        Assert.Equal(ExitCodes.Usage, await _test.RunAsync([.. _baseArguments, "--file", file, "--file-token", "ft_9", "--validate-only"]));
        Assert.Equal(ExitCodes.Usage, await _test.RunAsync([.. _baseArguments, "--file-token", "ft_unknown", "--validate-only"]));
        Assert.Empty(_mcp.Calls);
    }

    [Fact]
    public async Task Validate_only_stops_after_the_preview_and_prints_it_on_stdout()
    {
        var file = WriteCsv();
        QueueStaging();
        _mcp.ThenText(Preview(rows: 15, warnings: []));

        var exit = await _test.RunAsync([.. _baseArguments, "--file", file, "--validate-only"]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(2, _mcp.Calls.Count);
        JsonNode data = JsonNode.Parse(_test.Console.StdOut)!["result"]!["data"]!;
        Assert.Equal("Import Customers", (string?)data["scenario"]);
        Assert.Equal(15, (int?)data["file"]!["rows"]);
        Assert.False((bool)data["executed"]!);
    }

    [Fact]
    public async Task Run_without_a_terminal_and_without_yes_is_exit_2_CONFIRMATION_REQUIRED_after_the_preview()
    {
        var file = WriteCsv();
        QueueStaging();
        _mcp.ThenText(Preview(rows: 15, warnings: []));

        var exit = await _test.RunAsync([.. _baseArguments, "--file", file]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(2, _mcp.Calls.Count);
        Assert.Equal(ErrorCodes.ConfirmationRequired, (string?)JsonNode.Parse(_test.Console.StdOut)!["error"]!["code"]);
        Assert.Contains("--yes", (string?)JsonNode.Parse(_test.Console.StdOut)!["error"]!["nextStep"], StringComparison.Ordinal);
        Assert.Contains("15 rows", _test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_on_a_terminal_asks_and_runs_after_a_yes()
    {
        using TerminalServices terminal = TerminalServices.Create(input: "y\n");
        var file = Path.Combine(terminal.Directory, "customers.csv");
        File.WriteAllText(file, "Customer ID,Customer Name\nC1,Acme\n");
        terminal.Mcp.ThenText(Link("ut_import"))
            .ThenText(Preview(rows: 15, warnings: []))
            .ThenText(Done(Report(total: 15, processed: 15, failed: 0)));
        terminal.Http.ThenStatus(HttpStatusCode.OK);

        var exit = await terminal.RunAsync([.. _baseArguments, "--file", file, "--output", "json"]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(3, terminal.Mcp.Calls.Count);
        Assert.Contains(
            "Import 15 rows with scenario Import Customers into screen AR303000 on connection gab-dev? [y/N]",
            terminal.Console.StdErr,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_on_a_terminal_stops_after_a_no()
    {
        using TerminalServices terminal = TerminalServices.Create(input: "n\n");
        var file = Path.Combine(terminal.Directory, "customers.csv");
        File.WriteAllText(file, "Customer ID,Customer Name\nC1,Acme\n");
        terminal.Mcp.ThenText(Link("ut_import")).ThenText(Preview(rows: 15, warnings: []));
        terminal.Http.ThenStatus(HttpStatusCode.OK);

        var exit = await terminal.RunAsync([.. _baseArguments, "--file", file, "--output", "json"]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(2, terminal.Mcp.Calls.Count);
        Assert.Equal(ErrorCodes.ConfirmationDeclined, (string?)JsonNode.Parse(terminal.Console.StdOut)!["error"]!["code"]);
    }

    [Fact]
    public async Task A_report_with_failed_rows_still_exits_0_and_warns_to_read_the_erp_back()
    {
        var file = WriteCsv();
        QueueStaging();
        _mcp.ThenText(Preview(rows: 15, warnings: []))
            .ThenText(Done(Report(total: 15, processed: 13, failed: 2)));

        var exit = await _test.RunAsync([.. _baseArguments, "--file", file, "--yes"]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(2, (int?)JsonNode.Parse(_test.Console.StdOut)!["result"]!["data"]!["failed"]);
        Assert.Contains("warning: 2 of 15 rows failed.", _test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("Read the ERP back before reporting", _test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_spent_token_is_exit_5_and_the_next_step_says_to_read_the_ERP_first()
    {
        var file = WriteCsv();
        QueueStaging();
        _mcp.ThenText(Preview(rows: 15, warnings: []))
            .ThenText("ATTACHMENT_TOKEN_ALREADY_USED: this upload link has already been imported, so nothing was sent a second time.", isError: true);

        var exit = await _test.RunAsync([.. _baseArguments, "--file", file, "--yes"]);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        JsonNode error = JsonNode.Parse(_test.Console.StdOut)!["error"]!;
        Assert.Equal("ATTACHMENT_TOKEN_ALREADY_USED", (string?)error["code"]);
        Assert.Contains("Read the ERP before anything else", (string?)error["nextStep"], StringComparison.Ordinal);
        Assert.Contains("Read the ERP", (string?)error["nextStep"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_in_the_preview_is_exit_5_with_its_code_and_nothing_runs()
    {
        var file = WriteCsv();
        QueueStaging();
        _mcp.ThenText("IMPORT_COLUMNS_MISSING: the file does not have 'Customer Class', which 'Import Customers' reads.", isError: true);

        var exit = await _test.RunAsync([.. _baseArguments, "--file", file, "--yes"]);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal(2, _mcp.Calls.Count);
        Assert.Equal("IMPORT_COLUMNS_MISSING", (string?)JsonNode.Parse(_test.Console.StdOut)!["error"]!["code"]);
    }

    [Fact]
    public async Task A_long_import_is_followed_through_get_status()
    {
        var file = WriteCsv();
        QueueStaging();
        _mcp.ThenText(Preview(rows: 15, warnings: []))
            .ThenText("""{"taskId":"t_7","status":"running","progress":{"label":"Importing on AR303000","elapsedMs":30000}}""")
            .ThenText(Done(Report(total: 15, processed: 15, failed: 0)));

        var exit = await _test.RunAsync([.. _baseArguments, "--file", file, "--yes"]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            [McpToolNames.GetUploadUrl, McpToolNames.ExecuteImportScenario, McpToolNames.ExecuteImportScenario, McpToolNames.GetStatus],
            _mcp.Calls.Select(call => call.Name));
        Assert.Contains("Importing on AR303000", _test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_without_a_connection_is_exit_2_before_any_call()
    {
        var file = WriteCsv();

        var exit = await _test.RunAsync("import", "run", "--scenario", "Import Customers", "--file", file, "--intent", Intent, "--yes");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_mcp.Calls);
    }

    private const string Listing = """
        {"Scenarios":[
          {"Name":"Import Customers","ScreenID":"AR303000","ScreenTitle":"Customers","ApiEnabled":true,"Columns":["Customer ID","Customer Name"]},
          {"Name":"Locked Scenario","ScreenID":"XX000000","ScreenTitle":"Locked","ApiEnabled":false,"Columns":null}
        ]}
        """;

    private static string Link(string token) =>
        $$"""{"fileToken":"{{token}}","uploadUrl":"https://nx-dev.velixo.com/mcp/upload/{{token}}","expiresAt":"2026-10-28T16:10:00+00:00"}""";

    /// <summary>The preview as the server sends it: plain text holding <c>{ textualResponse }</c>, not a task.</summary>
    private static string Preview(int rows, string[] warnings)
    {
        var inner = new JsonObject
        {
            ["scenario"] = "Import Customers",
            ["screenId"] = "AR303000",
            ["screenTitle"] = "Customers",
            ["file"] = new JsonObject { ["name"] = "customers.csv", ["sheet"] = null, ["range"] = null, ["rows"] = rows },
            ["columns"] = new JsonObject { ["matched"] = new JsonArray(), ["missing"] = new JsonArray(), ["ignored"] = new JsonArray() },
            ["sample"] = new JsonArray(),
            ["warnings"] = new JsonArray(warnings.Select(warning => (JsonNode?)warning).ToArray()),
            ["executed"] = false,
        };
        return new JsonObject { ["textualResponse"] = inner.ToJsonString() }.ToJsonString();
    }

    private static JsonObject Report(int total, int processed, int failed)
    {
        var rows = new JsonArray();
        for (var row = 0; row < total; row++)
        {
            var ok = row < processed;
            rows.Add((JsonNode)new JsonObject
            {
                ["row"] = row + 2,
                ["processed"] = ok,
                ["keys"] = ok ? new JsonObject { ["CustomerID"] = $"CA{row:000000}" } : null,
                ["error"] = ok ? null : "'Customer Class' cannot be empty.",
            });
        }

        return new JsonObject
        {
            ["scenario"] = "Import Customers",
            ["screenId"] = "AR303000",
            ["executed"] = true,
            ["totalRows"] = total,
            ["processed"] = processed,
            ["failed"] = failed,
            ["notAttempted"] = 0,
            ["rows"] = rows,
            ["warnings"] = new JsonArray(),
        };
    }

    private static string Done(JsonObject report) =>
        new JsonObject
        {
            ["taskId"] = "t_7",
            ["status"] = "done",
            ["result"] = new JsonObject { ["textualResponse"] = report.ToJsonString() },
        }.ToJsonString();

    /// <summary>Queues the link <c>get_upload_url</c> answers with and the upload endpoint's acceptance.</summary>
    private void QueueStaging()
    {
        _mcp.ThenText(Link("ut_import"));
        _test.Http.ThenStatus(HttpStatusCode.OK);
    }

    private string WriteCsv()
    {
        var path = Path.Combine(_test.Directory, "customers.csv");
        File.WriteAllText(path, "Customer ID,Customer Name\nC1,Acme\n");
        return path;
    }

    /// <summary>Services whose console is a terminal with scripted input, so the confirmation prompt can be answered.</summary>
    private sealed class TerminalServices : IDisposable
    {
        private readonly TempDirectory _directory = new();

        private TerminalServices(string input)
        {
            Console = new TestConsole(redirected: false, input);
            var environment = new TestEnvironment(_directory.Path);
            environment.Variables[EnvironmentVariables.ConfigDirectory] = System.IO.Path.Combine(_directory.Path, "config");
            ConfigPaths paths = ConfigPaths.Resolve(environment);
            Http = new FakeHttpHandler();
            Services = new CliServices(
                Console,
                environment,
                TimeProvider.System,
                new ProfileStore(paths),
                new TokenStore(paths),
                new HttpClient(Http),
                new NoBrowser());
            Services.McpClientFactory = _ => Mcp;
        }

        public TestConsole Console { get; }

        public FakeHttpHandler Http { get; }

        public FakeMcpClient Mcp { get; } = new();

        public CliServices Services { get; }

        public string Directory => _directory.Path;

        public static TerminalServices Create(string input) => new(input);

        public Task<int> RunAsync(params string[] args) => Program.RunAsync(args, Services);

        public void Dispose()
        {
            Services.Http.Dispose();
            _directory.Dispose();
        }

        private sealed class NoBrowser : IBrowserOpener
        {
            public bool TryOpen(Uri url) => false;
        }
    }
}
