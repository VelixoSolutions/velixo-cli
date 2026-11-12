using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.IntegrationTests;

/// <summary>
/// The commands that read no ERP data: sign-in, connections, the server's tools, notifications, the
/// configuration, the guide and the version, with the shared flags. The ERP commands are in
/// <see cref="QueryTests"/>, <see cref="ScreenTests"/>, <see cref="FileAndImportTests"/> and the classes
/// they name. The one message sent goes to the signed-in user. The tests run one at a time
/// (<c>xunit.runner.json</c>), because each run of the binary may rotate the refresh token, and two
/// rotations of one token at once end the session.
/// </summary>
public sealed class LiveTests
{
    // Sign-in

    [LiveFact]
    [Covers("status")]
    [CoversFlags(CoversFlagsAttribute.Shared, "--profile", "--env", "--non-interactive", "--output")]
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
    [Covers("status")]
    [CoversFlags(CoversFlagsAttribute.Shared, "--url")]
    public async Task A_server_named_by_its_address_is_the_environment_it_belongs_to()
    {
        // The address without a path: the CLI adds /mcp, and the sign-in of the environment applies.
        Uri endpoint = Velixo.Cli.Config.Environments.McpUrls[LiveCli.Environment!];
        var address = endpoint.GetLeftPart(UriPartial.Authority);

        CliRun run = await LiveCli.RunAsync("status", "--url", address);

        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        Assert.Equal(endpoint.ToString(), (string?)run.Json!["server"]!["url"]);
        Assert.True((bool?)run.Json!["signedIn"], run.ToString());
    }

    [LiveFact]
    [Covers("whoami")]
    [CoversFlags(CoversFlagsAttribute.Shared, "--compact", "--no-color")]
    public async Task Compact_prints_the_document_on_one_line_without_color()
    {
        CliRun run = await LiveCli.RunAsync("whoami", "--compact", "--no-color");

        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        Assert.Single(run.StdOut.TrimEnd().Split('\n'));
        Assert.NotNull(run.Json?["user"]);
        Assert.DoesNotContain("\u001b[", run.StdOut + run.StdErr, StringComparison.Ordinal);
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
    [CoversFlags("login", "--no-browser", "--complete")]
    public async Task Login_without_a_browser_gets_a_device_code_from_the_gateway()
    {
        var config = LiveCli.NewConfigDirectory();

        // Completing a sign-in that was never started is refused at once, without waiting.
        CliRun early = await LiveCli.RunInAsync(config, "login", "--complete");
        Assert.True(early.ExitCode == ExitCodes.Usage, early.ToString());

        CliRun start = await LiveCli.RunInAsync(config, "login", "--no-browser");
        Assert.True(start.ExitCode == ExitCodes.Success, start.ToString());
        Assert.Equal("authorization_required", (string?)start.Json!["status"]);
        Assert.StartsWith("https://", (string?)start.Json!["verificationUri"], StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty((string?)start.Json!["userCode"]), start.ToString());
        Assert.EndsWith("--complete", (string?)start.Json!["nextStep"], StringComparison.Ordinal);
    }

    [LiveFact]
    [Covers("logout")]
    [CoversFlags("logout", "--all")]
    public async Task Logout_of_profiles_that_never_signed_in_succeeds()
    {
        var config = LiveCli.NewConfigDirectory();

        CliRun run = await LiveCli.RunInAsync(config, "logout");
        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        Assert.True((bool?)run.Json!["signedOut"], run.ToString());

        // Every profile of a configuration of its own: the developer's and the run's sign-ins are untouched.
        CliRun all = await LiveCli.RunInAsync(config, "logout", "--all");
        Assert.True(all.ExitCode == ExitCodes.Success, all.ToString());
        Assert.True((bool?)all.Json!["signedOut"], all.ToString());
        Assert.True(all.Json!["profiles"] is JsonArray, all.ToString());
    }

    // Connections and the raw tools

    [LiveFact]
    [Covers("connections list")]
    [CoversFlags("connections list", "--status")]
    public async Task The_test_connections_are_listed_as_working()
    {
        CliRun run = await LiveCli.RunAsync("connections", "list", "--status", "ok");

        Assert.True(run.ExitCode == ExitCodes.Success, run.ToString());
        JsonArray connections = run.Json!.AsArray();
        Assert.All(connections, entry => Assert.Equal("ok", (string?)entry?["status"]));
        foreach ((Erp erp, string type) in new[] { (Erp.Acumatica, "acumatica"), (Erp.SageIntacct, "sage_intacct"), (Erp.BusinessCentral, "business_central") })
        {
            JsonNode? connection = connections.FirstOrDefault(entry => (string?)entry?["name"] == LiveCli.ConnectionOf(erp));
            Assert.True(connection is not null, $"{LiveCli.ConnectionOf(erp)} is not a working connection.\n{run}");
            Assert.Equal(type, (string?)connection["type"]);
        }

        Assert.Contains(connections, entry => (string?)entry?["name"] == LiveCli.WriteConnection && (string?)entry?["type"] == "acumatica");
    }

    [LiveFact]
    [Covers("query run")]
    [CoversFlags(CoversFlagsAttribute.Shared, "--connection")]
    public async Task An_unknown_connection_is_refused()
    {
        CliRun run = await LiveCli.RunAsync(
            "query", "run", "--query", LiveCli.Fixture(Erp.Acumatica, "customers.vql"), "--connection", "velixo-it-no-such-connection");

        Assert.True(run.ExitCode is ExitCodes.Usage or ExitCodes.ServerRefused, run.ToString());
        Assert.Contains("velixo-it-no-such-connection", run.StdOut, StringComparison.Ordinal);
    }

    [LiveFact]
    [Covers("connections switch")]
    [CoversFlags("connections switch", "--clear")]
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

        // --clear calls no tool, so it works in a configuration that never signed in.
        CliRun cleared = await LiveCli.RunSignedOutAsync("connections", "switch", "--clear");
        Assert.True(cleared.ExitCode == ExitCodes.Success, cleared.ToString());
        Assert.Null(cleared.Json!["connection"]);
    }

    [LiveFact]
    [Covers("tools list")]
    [CoversFlags("tools list", "--names")]
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
    [Covers("notify")]
    [CoversFlags("notify", "--subject", "--body", "--severity", "--target", "--payload", "--link", "--link-label", "--source-name")]
    public async Task A_notification_with_a_query_result_goes_to_the_signed_in_user()
    {
        // The signed-in user, named as a target: the one message the suite sends goes to whoever runs it.
        CliRun whoami = (await LiveCli.RunAsync("whoami")).Expect();
        var email = (string?)whoami.Json!["user"]!["email"];
        Assert.False(string.IsNullOrEmpty(email), whoami.ToString());

        var payload = LiveCli.TempFile("notify.customers.json");
        CliRun customers = (await LiveCli.RunAsync(
            "query", "run", "--query", LiveCli.Fixture(Erp.Acumatica, "customers.vql"), "--top", "3", "--connection", LiveCli.Connection)).Expect();
        await File.WriteAllTextAsync(payload, customers.StdOut, TestContext.Current.CancellationToken);

        CliRun run = (await LiveCli.RunAsync(
            "notify", "--subject", "Velixo CLI integration test", "--body", "A test of velixo notify, with three customers. Nothing to do.",
            "--severity", "Info", "--target", email!, "--payload", payload, "--link", "https://cli.velixo.com", "--link-label", "Velixo CLI",
            "--source-name", "velixo-cli-integration-tests")).Expect();

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
    [CoversFlags("help", "--all")]
    [CoversFlags("version", "--check")]
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

        // The latest release comes from GitHub, with the command that updates this install.
        CliRun check = await LiveCli.RunSignedOutAsync("version", "--check");
        Assert.True(check.ExitCode == ExitCodes.Success, check.ToString());
        Assert.False(string.IsNullOrEmpty((string?)check.Json!["latest"]), check.ToString());
        Assert.True(check.Json!["updateAvailable"] is JsonValue, check.ToString());
    }
}
