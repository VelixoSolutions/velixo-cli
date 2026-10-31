using System.CommandLine;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.IntegrationTests;

/// <summary>A test against the live environment, skipped unless <c>VELIXO_IT_ENV</c> names one.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = "Set VELIXO_IT_ENV to run the live tests.";
        SkipUnless = nameof(LiveCli.Enabled);
        SkipType = typeof(LiveCli);
    }
}

/// <summary>
/// Every command of the CLI against a live environment and the connection <see cref="LiveCli.Connection"/>.
/// Nothing writes to the ERP: a write batch runs only as <c>--dry-run</c> and without <c>--yes</c>, an
/// import only as <c>--validate-only</c>. The one message sent goes to the signed-in user. The tests run
/// one at a time (<c>xunit.runner.json</c>), because each run of the binary may rotate the refresh token,
/// and two rotations of one token at once end the session.
/// </summary>
public sealed class LiveTests
{
    private const string TestVendor = "VELIXOIT";

    /// <summary>The key a build's output carries its number in the profile's list under.</summary>
    private const string LibraryNumber = "libraryNumber";

    /// <summary>The customer plan, built with <c>--no-wait</c> and collected with <c>task status</c>, shared by the tests that need a plan.</summary>
    private static readonly Lazy<Task<string>> _plan = new(BuildPlanAsync);

    /// <summary>The first customers the plan returns; the first column of each row is the customer ID.</summary>
    private static readonly Lazy<Task<JsonArray>> _customers = new(RunPlanAsync);

    [Fact]
    public void Every_command_has_a_live_test()
    {
        Command root = Program.BuildRootCommand(CliServices.CreateDefault());
        var covered = typeof(LiveTests).GetMethods()
            .SelectMany(method => method.GetCustomAttribute<CoversAttribute>()?.Commands ?? [])
            .ToHashSet(StringComparer.Ordinal);

        var missing = Leaves(root, string.Empty).Where(command => !covered.Contains(command)).ToList();
        var unknown = covered.Except(Leaves(root, string.Empty)).ToList();

        Assert.Empty(missing);
        Assert.Empty(unknown);
    }

    // Sign-in

    [LiveFact]
    [Covers("status")]
    public async Task The_setup_is_ready_and_a_machine_that_never_signed_in_is_told_to_sign_in()
    {
        CliRun run = await LiveCli.RunAsync("status");
        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        Assert.True((bool?)run.Json!["server"]!["reachable"], run.ToString());
        Assert.Empty(run.Json!["problems"]!.AsArray());

        CliRun signedOut = await LiveCli.RunSignedOutAsync("status");
        Assert.True(signedOut.ExitCode == ExitCodes.AuthenticationRequired, signedOut.ToString());
    }

    [LiveFact]
    [Covers("whoami")]
    public async Task Whoami_reads_the_stored_sign_in()
    {
        CliRun run = await LiveCli.RunAsync("whoami");

        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        Assert.NotNull(run.Json);
    }

    [LiveFact]
    [Covers("auth token")]
    public async Task Auth_token_prints_the_refresh_token_and_nothing_else()
    {
        CliRun run = await LiveCli.RunAsync("auth", "token");

        // The token is a secret: the messages name the exit code and the length, never stdout.
        Assert.True(run.ExitCode == ExitCodes.Success, $"exit {run.ExitCode}\n{run.StdErr}");
        var token = run.StdOut.Trim();
        Assert.True(token.Length > 20 && !token.Contains(' ', StringComparison.Ordinal), $"Printed {token.Length} characters, not one token.");
    }

    [LiveFact]
    [Covers("login")]
    public async Task Login_without_a_browser_opens_the_gateway_page_and_refuses_a_bad_code()
    {
        var config = LiveCli.NewConfigDirectory();

        CliRun start = await LiveCli.RunInAsync(config, "login", "--no-browser");
        Assert.True(start.ExitCode == ExitCodes.Success, start.ToString());
        Assert.Equal("authorization_required", (string?)start.Json!["status"]);
        Assert.StartsWith("https://", (string?)start.Json!["browserUrl"], StringComparison.Ordinal);

        CliRun complete = await LiveCli.RunInAsync(config, "login", "--complete", "http://127.0.0.1:1/callback?code=not-a-code&state=not-the-state");
        Assert.True(complete.ExitCode != ExitCodes.Success, complete.ToString());
    }

    [LiveFact]
    [Covers("logout")]
    public async Task Logout_of_a_profile_that_never_signed_in_succeeds()
    {
        CliRun run = await LiveCli.RunSignedOutAsync("logout");

        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        Assert.True((bool?)run.Json!["signedOut"], run.ToString());
    }

    // Connections and the raw tools

    [LiveFact]
    [Covers("connections list")]
    public async Task The_test_connection_is_listed_and_usable()
    {
        CliRun run = await LiveCli.RunAsync("connections", "list");

        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        JsonNode? connection = run.Json!.AsArray().FirstOrDefault(entry => (string?)entry?["name"] == LiveCli.Connection);
        Assert.True(connection is not null, run.ToString());
        Assert.Equal("ok", (string?)connection["status"]);
    }

    [LiveFact]
    [Covers("connections switch")]
    public async Task Switch_sets_the_default_refuses_an_unknown_name_and_puts_the_old_default_back()
    {
        CliRun before = await LiveCli.RunAsync("config", "get", $"profiles.{LiveCli.Profile}.connection");
        Assert.True(before.ExitCode == ExitCodes.Success, before.ToString());
        var original = (string?)before.Json!["value"];

        try
        {
            CliRun unknown = await LiveCli.RunAsync("connections", "switch", "velixo-it-no-such-connection");
            Assert.True(unknown.ExitCode == ExitCodes.Usage, unknown.ToString());

            CliRun switched = await LiveCli.RunAsync("connections", "switch", LiveCli.Connection);
            Assert.True(switched.ExitCode == ExitCodes.Success, switched.ToString());
            Assert.Equal(LiveCli.Connection, (string?)switched.Json!["connection"]);
        }
        finally
        {
            CliRun restored = original is null
                ? await LiveCli.RunAsync("connections", "switch", "--clear")
                : await LiveCli.RunAsync("connections", "switch", original);
            Assert.True(restored.ExitCode == ExitCodes.Success, restored.ToString());
        }
    }

    [LiveFact]
    [Covers("tools list")]
    public async Task The_server_offers_every_tool_the_commands_call()
    {
        CliRun run = await LiveCli.RunAsync("tools", "list", "--names");

        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        var offered = run.Json!.AsArray().Select(name => (string?)name).ToHashSet(StringComparer.Ordinal);
        string[] called =
        [
            McpToolNames.ListConnections, McpToolNames.ToolGuidance, McpToolNames.PrepareQuery, McpToolNames.ExecuteQuery,
            McpToolNames.CreateWorkbookFromQuery, McpToolNames.PrepareCommands, McpToolNames.ExecuteCommands, McpToolNames.GetStatus,
            McpToolNames.GetUploadUrl, McpToolNames.ListImportScenarios, McpToolNames.ExecuteImportScenario,
            McpToolNames.SendNotification, McpToolNames.SignOut,
        ];
        var missing = called.Where(tool => !offered.Contains(tool)).ToList();
        Assert.Empty(missing);
    }

    [LiveFact]
    [Covers("tools guidance")]
    public async Task The_servers_guidance_comes_back()
    {
        CliRun run = await LiveCli.RunAsync("tools", "guidance", "query", "run");

        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        JsonArray sections = run.Json!["sections"]!.AsArray();
        Assert.Contains(sections, section => (string?)section?["name"] == McpToolNames.ExecuteQuery);
    }

    [LiveFact]
    [Covers("query build", "task status", "query run")]
    public async Task A_build_followed_as_a_task_gives_a_plan_that_runs()
    {
        JsonArray rows = await _customers.Value;

        Assert.InRange(rows.Count, 1, 5);
    }

    [LiveFact]
    [Covers("query workbook", "files download")]
    public async Task A_plan_becomes_a_workbook_that_downloads()
    {
        var resultFile = LiveCli.TempFile("customers.workbook.json");
        var directory = Directory.CreateTempSubdirectory("velixo-it-downloads-").FullName;

        CliRun workbook = await LiveCli.RunAsync("query", "workbook", "--plan", await _plan.Value, "--connection", LiveCli.Connection);
        Assert.True(workbook.ExitCode == ExitCodes.Success, workbook.ToString());
        File.WriteAllText(resultFile, workbook.StdOut);

        CliRun download = await LiveCli.RunAsync("files", "download", "--from", resultFile, "--dir", directory);
        Assert.True(download.ExitCode == ExitCodes.Success, download.ToString());
        JsonNode file = download.Json!.AsArray()[0]!;
        Assert.True(File.Exists((string?)file["path"]), download.ToString());
        Assert.True(new FileInfo((string)file["path"]!).Length > 0, download.ToString());
    }

    // Screens

    [LiveFact]
    [Covers("command build", "command run", "command list", "command delete")]
    public async Task A_screen_read_builds_runs_by_its_number_and_leaves_the_list()
    {
        var customerId = (string?)(await _customers.Value)[0]![0];
        Assert.False(string.IsNullOrEmpty(customerId));

        CliRun build = await LiveCli.RunAsync(
            "command", "build", "Read the name and the status of customer {{CustomerID}} on the Customers screen", "--connection", LiveCli.Connection);
        Assert.True(build.ExitCode == ExitCodes.Success, build.ToString());

        // The run needs --yes, because loading a record by its key is a Set. Before giving it, make sure the
        // batch can only load and read: no Save, no action, no other command that changes the record.
        var types = CommandTypes(build);
        Assert.True(types.All(type => type is "Get" or "Clear" or "Set"), $"The batch does more than read: {string.Join(", ", types)}\n{build}");

        var number = ((int)build.Json![LibraryNumber]!).ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            CliRun listed = await LiveCli.RunAsync("command", "list");
            Assert.True(listed.ExitCode == ExitCodes.Success, listed.ToString());
            Assert.Contains(listed.Json!.AsArray(), entry => (string?)entry?["number"]?.ToString() == number);

            // By its number: the entry carries the connection it was built on.
            CliRun run = await LiveCli.RunAsync("command", "run", number, "--param", $"CustomerID={customerId}", "--yes");
            Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
            Assert.Contains(customerId!, run.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            CliRun deleted = await LiveCli.RunAsync("command", "delete", number);
            Assert.True(deleted.ExitCode == ExitCodes.Success, deleted.ToString());
        }

        CliRun after = await LiveCli.RunAsync("command", "list");
        Assert.DoesNotContain(after.Json!.AsArray(), entry => (string?)entry?["number"]?.ToString() == number);
    }

    [LiveFact]
    [Covers("query list", "query delete")]
    public async Task A_query_build_is_listed_runs_by_its_number_and_is_deleted()
    {
        CliRun build = await LiveCli.RunAsync("query", "build", "List the active customers with their customer ID, sorted by customer ID", "--connection", LiveCli.Connection);
        Assert.True(build.ExitCode == ExitCodes.Success, build.ToString());
        var number = ((int)build.Json![LibraryNumber]!).ToString(System.Globalization.CultureInfo.InvariantCulture);

        try
        {
            CliRun listed = await LiveCli.RunAsync("query", "list");
            Assert.True(listed.ExitCode == ExitCodes.Success, listed.ToString());
            Assert.Contains(listed.Json!.AsArray(), entry => (string?)entry?["number"]?.ToString() == number && (string?)entry?["connection"] == LiveCli.Connection);

            CliRun run = await LiveCli.RunAsync("query", "run", number, "--top", "3");
            Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        }
        finally
        {
            CliRun deleted = await LiveCli.RunAsync("query", "delete", number);
            Assert.True(deleted.ExitCode == ExitCodes.Success, deleted.ToString());
        }

        CliRun missing = await LiveCli.RunAsync("query", "run", number);
        Assert.True(missing.ExitCode == ExitCodes.Usage, missing.ToString());
    }

    [LiveFact]
    [Covers("command build", "command run")]
    public async Task A_screen_write_passes_the_dry_run_and_is_refused_without_yes()
    {
        var customerId = (string?)(await _customers.Value)[0]![0];
        var batchFile = LiveCli.TempFile("customer-rename.batch.json");

        CliRun build = await LiveCli.RunAsync(
            "command", "build", "Change the name of customer {{CustomerID}} to {{CustomerName}} on the Customers screen and save it", "--connection", LiveCli.Connection);
        Assert.True(build.ExitCode == ExitCodes.Success, build.ToString());
        File.WriteAllText(batchFile, build.StdOut);
        Assert.Contains(CommandTypes(build), type => type is not ("Get" or "Clear" or "Set"));

        string[] values = ["--param", $"CustomerID={customerId}", "--param", "CustomerName=Velixo CLI test, never saved", "--connection", LiveCli.Connection];

        CliRun dryRun = await LiveCli.RunAsync(["command", "run", "--batch", batchFile, .. values, "--dry-run"]);
        Assert.True(dryRun.ExitCode == ExitCodes.Success, dryRun.ToString());
        Assert.True((bool?)dryRun.Json!["dryRun"], dryRun.ToString());

        CliRun refused = await LiveCli.RunAsync(["command", "run", "--batch", batchFile, .. values]);
        Assert.True(refused.ExitCode == ExitCodes.Usage, refused.ToString());
        Assert.Equal(ErrorCodes.ConfirmationRequired, (string?)refused.Json!["error"]!["code"]);

        await LiveCli.RunAsync("command", "delete", ((int)build.Json![LibraryNumber]!).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // Files, imports and notifications

    [LiveFact]
    [Covers("files stage")]
    public async Task A_file_stages_by_upload_and_by_a_url_that_takes_its_bytes_once()
    {
        var file = LiveCli.TempFile("velixo-it-stage.csv");
        File.WriteAllText(file, "Name,Amount\nVelixo CLI test,1\n");

        CliRun uploaded = await LiveCli.RunAsync("files", "stage", file);
        Assert.True(uploaded.ExitCode == ExitCodes.Success, uploaded.ToString());
        Assert.True((bool?)uploaded.Json!["uploaded"], uploaded.ToString());
        Assert.False(string.IsNullOrEmpty((string?)uploaded.Json!["fileToken"]), uploaded.ToString());

        // The curl route: the URL takes the bytes with a plain PUT, as `curl -T FILE URL` sends them.
        CliRun linked = await LiveCli.RunAsync("files", "stage", file, "--no-upload");
        Assert.True(linked.ExitCode == ExitCodes.Success, linked.ToString());
        var url = (string?)linked.Json!["uploadUrl"];
        Assert.False(string.IsNullOrEmpty(url), "The output has no uploadUrl.");
        using var http = new HttpClient();
        using HttpResponseMessage put = await http.PutAsync(url, new ByteArrayContent(File.ReadAllBytes(file)), TestContext.Current.CancellationToken);
        Assert.True(put.IsSuccessStatusCode, $"The upload URL answered HTTP {(int)put.StatusCode}.");
    }

    [LiveFact]
    [Covers("import scenarios")]
    public async Task The_import_scenarios_are_listed()
    {
        CliRun run = await LiveCli.RunAsync("import", "scenarios", "--connection", LiveCli.Connection);

        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        Assert.NotEmpty(run.Json!.AsArray());
    }

    [LiveFact]
    [Covers("import run")]
    public async Task An_import_validates_without_running()
    {
        const string scenario = "ACU Import Vendors";
        CliRun scenarios = await LiveCli.RunAsync("import", "scenarios", "--connection", LiveCli.Connection, "--filter", scenario);
        Assert.True(scenarios.ExitCode == ExitCodes.Success, scenarios.ToString());
        JsonNode? vendors = scenarios.Json!.AsArray().FirstOrDefault(entry => (string?)entry?["name"] == scenario);
        Assert.True(vendors?["columns"] is JsonArray, scenarios.ToString());

        var columns = vendors!["columns"]!.AsArray().Select(column => column is JsonValue ? (string)column! : (string)column!["name"]!).ToList();
        var values = columns.Select(column => column switch
        {
            "Vendor ID" => TestVendor,
            "Description" => "Velixo CLI integration test, never imported",
            _ => string.Empty,
        });
        var file = LiveCli.TempFile("velixo-it-vendors.csv");
        File.WriteAllText(file, string.Join(',', columns.Select(Csv)) + "\n" + string.Join(',', values.Select(Csv)) + "\n");

        CliRun run = await LiveCli.RunAsync("import", "run", "--connection", LiveCli.Connection, "--scenario", scenario, "--file", file, "--validate-only");

        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        Assert.False((bool?)run.Json!["result"]!["data"]!["executed"], run.ToString());
    }

    [LiveFact]
    [Covers("notify")]
    public async Task A_notification_goes_to_the_signed_in_user()
    {
        CliRun run = await LiveCli.RunAsync(
            "notify", "--subject", "Velixo CLI integration test", "--body", "A test of velixo notify. Nothing to do.", "--severity", "Info");

        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        Assert.True((bool?)run.Json!["accepted"], run.ToString());
    }

    // Commands that need no sign-in, in a configuration folder of their own

    [LiveFact]
    [Covers("config set", "config get", "config list", "config path")]
    public async Task The_configuration_reads_and_writes_its_own_folder()
    {
        var config = LiveCli.NewConfigDirectory();

        CliRun set = await LiveCli.RunInAsync(config, "config", "set", "defaultOutput", "table");
        Assert.True(set.ExitCode == ExitCodes.Success, set.ToString());

        CliRun get = await LiveCli.RunInAsync(config, "config", "get", "defaultOutput");
        Assert.True(get.ExitCode == ExitCodes.Success, get.ToString());
        Assert.Contains("table", get.StdOut, StringComparison.Ordinal);

        CliRun list = await LiveCli.RunInAsync(config, "config", "list");
        Assert.True(list.ExitCode == ExitCodes.Success, list.ToString());
        Assert.Contains("defaultOutput", list.StdOut, StringComparison.Ordinal);

        CliRun path = await LiveCli.RunInAsync(config, "config", "path");
        Assert.True(path.ExitCode == ExitCodes.Success, path.ToString());
        Assert.Contains(Path.GetFileName(config), path.StdOut, StringComparison.Ordinal);
    }

    [LiveFact]
    [Covers("guide", "help", "version")]
    public async Task The_guide_the_help_and_the_version_need_no_sign_in()
    {
        CliRun guide = await LiveCli.RunSignedOutAsync("guide", "query", "run");
        Assert.True(guide.ExitCode == ExitCodes.Success, guide.ToString());
        Assert.Equal("query-run", (string?)guide.Json!["sections"]![0]!["name"]);

        CliRun help = await LiveCli.RunSignedOutAsync("help", "--all");
        Assert.True(help.ExitCode == ExitCodes.Success, help.ToString());
        Assert.NotEmpty(help.Json!["commands"]!.AsArray());

        CliRun version = await LiveCli.RunSignedOutAsync("version");
        Assert.True(version.ExitCode == ExitCodes.Success, version.ToString());
        Assert.Contains(CliInfo.Version, version.StdOut, StringComparison.Ordinal);
    }

    /// <summary>Builds the customer list with <c>--no-wait</c>, follows it with <c>task status --wait</c>, and saves the plan.</summary>
    private static async Task<string> BuildPlanAsync()
    {
        var planFile = LiveCli.TempFile("customers.plan.json");

        CliRun started = await LiveCli.RunAsync(
            "query", "build", "List the active customers: the customer ID first, then the customer name, sorted by customer ID",
            "--connection", LiveCli.Connection, "--no-wait");
        Assert.True(started.ExitCode == ExitCodes.Success, started.ToString());
        var taskId = (string?)started.Json!["taskId"];
        Assert.False(string.IsNullOrEmpty(taskId), started.ToString());

        // A build that finished inside its first call is kept in the profile's list; the tests leave the list as they found it.
        if (started.Json!["libraryNumber"] is JsonValue kept)
        {
            await LiveCli.RunAsync("query", "delete", kept.ToString());
        }

        CliRun followed = await LiveCli.RunAsync("task", "status", taskId!, "--wait");
        Assert.True(followed.ExitCode == ExitCodes.Success, followed.ToString());
        Assert.True(followed.Json!["result"]?["data"]?["queries"] is JsonArray, followed.ToString());
        File.WriteAllText(planFile, followed.StdOut);
        return planFile;
    }

    private static async Task<JsonArray> RunPlanAsync()
    {
        CliRun run = await LiveCli.RunAsync("query", "run", "--plan", await _plan.Value, "--top", "5", "--connection", LiveCli.Connection);
        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());

        JsonNode result = run.Json!["results"] is JsonArray results ? results[0]!["result"]! : run.Json!;
        return result["result"]!["data"]!["rows"]!.AsArray();
    }

    private static List<string?> CommandTypes(CliRun build)
    {
        JsonObject batch = build.Json!["result"]!["data"]!.AsObject();
        return new[] { "commands", "lineCommands", "splitCommands", "finishCommands" }
            .SelectMany(set => batch[set] as JsonArray ?? [])
            .Select(command => (string?)command?["type"])
            .ToList();
    }

    private static string Csv(string value) =>
        value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;

    private static IEnumerable<string> Leaves(Command command, string path) =>
        command.Subcommands.Count == 0
            ? [path]
            : command.Subcommands.Where(child => !child.Hidden).SelectMany(child => Leaves(child, (path + " " + child.Name).Trim()));
}
