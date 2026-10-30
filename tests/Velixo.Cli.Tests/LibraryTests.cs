using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Screen;

namespace Velixo.Cli.Tests;

public sealed class LibraryTests
{
    private const string Plan = """{"queries":[{"purpose":"AR balance","giql":"DECLARE [AsOf] SCHEMA [PX.Objects.AR.ARInvoice].DocDate AS 'As Of Date' REQUIRED\nFROM A SELECT X"}]}""";

    private const string OtherPlan = """{"queries":[{"purpose":"AP balance","giql":"DECLARE [AsOf] SCHEMA [PX.Objects.AP.APInvoice].DocDate AS 'As Of Date' REQUIRED\nFROM B SELECT Y"}]}""";

    private const string NoRows = """{"columns":[],"rows":[],"totalCount":0}""";

    [Fact]
    public async Task A_build_is_kept_under_the_next_number_with_its_title_and_connection()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText(ScreenHarness.Done(Plan)).ThenText(ScreenHarness.Done(OtherPlan));

        await test.RunAsync("query", "build", "AR balance as of {{AsOfDate}}", "--connection", "gab-dev", "--intent", ScreenHarness.Intent);
        Assert.Equal(1, (int)test.Output[LibraryCommandsNumberKey]!);
        test.Console.OutWriter.GetStringBuilder().Clear();
        await test.RunAsync("query", "build", "AP balance as of {{AsOfDate}}", "--intent", ScreenHarness.Intent);

        Assert.Equal(2, (int)test.Output[LibraryCommandsNumberKey]!);
        List<LibraryEntry> queries = test.Profiles.LoadLibrary("prod").Queries;
        Assert.Equal(["AR balance", "AP balance"], queries.Select(entry => entry.Title));
        Assert.Equal("gab-dev", queries[0].Connection);
        Assert.Contains("Saved as query 2", test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_build_started_without_waiting_is_not_kept()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText("""{"taskId":"t_9","status":"running"}""");

        var exit = await test.RunAsync("query", "build", "AR balance", "--no-wait", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(test.Profiles.LoadLibrary("prod").Queries);
        Assert.Null(test.Output[LibraryCommandsNumberKey]);
    }

    [Fact]
    public async Task A_run_by_number_uses_the_entrys_connection_unless_the_flag_names_another()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText(ScreenHarness.Done(Plan)).ThenText(ScreenHarness.Done(NoRows)).ThenText(ScreenHarness.Done(NoRows));
        await test.RunAsync("query", "build", "AR balance as of {{AsOfDate}}", "--connection", "gab-dev", "--intent", ScreenHarness.Intent);

        var exit = await test.RunAsync("query", "run", "1", "--param", "As Of Date=2026-09-30", "--intent", ScreenHarness.Intent);
        await test.RunAsync("query", "run", "1", "--param", "As Of Date=2026-09-30", "--connection", "skyline", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("gab-dev", (string?)test.Mcp.Calls[1].Arguments["connectionName"]);
        Assert.StartsWith("DECLARE [AsOf]", (string?)test.Mcp.Calls[1].Arguments["giql"], StringComparison.Ordinal);
        Assert.Equal("skyline", (string?)test.Mcp.Calls[2].Arguments["connectionName"]);
    }

    [Fact]
    public async Task A_plan_of_one_query_run_by_number_or_name_prints_the_single_query_document()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText(ScreenHarness.Done(Plan)).ThenText(ScreenHarness.Done(NoRows)).ThenText(ScreenHarness.Done(NoRows));
        await test.RunAsync("query", "build", "AR balance as of {{AsOfDate}}", "--name", "ar", "--intent", ScreenHarness.Intent);

        foreach (var reference in new[] { "1", "ar" })
        {
            test.Console.OutWriter.GetStringBuilder().Clear();
            var exit = await test.RunAsync("query", "run", reference, "--param", "As Of Date=2026-09-30", "--intent", ScreenHarness.Intent);

            Assert.Equal(ExitCodes.Success, exit);
            Assert.Null(test.Output["results"]);
            Assert.Equal(0, (int?)test.Output["result"]!["data"]!["totalCount"]);
        }
    }

    [Fact]
    public async Task List_shows_the_entries_and_a_deleted_number_is_never_reused()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText(ScreenHarness.Done(Plan)).ThenText(ScreenHarness.Done(OtherPlan)).ThenText(ScreenHarness.Done(Plan));
        await test.RunAsync("query", "build", "AR", "--intent", ScreenHarness.Intent);
        await test.RunAsync("query", "build", "AP", "--intent", ScreenHarness.Intent);

        var deleted = await test.RunAsync("query", "delete", "1");
        await test.RunAsync("query", "build", "AR again", "--intent", ScreenHarness.Intent);
        test.Console.OutWriter.GetStringBuilder().Clear();
        await test.RunAsync("query", "list");

        Assert.Equal(ExitCodes.Success, deleted);
        JsonArray listed = test.Output.AsArray();
        Assert.Equal([2, 3], listed.Select(entry => (int)entry!["number"]!));
        Assert.Equal("As Of Date", (string?)listed[0]!["values"]);
    }

    [Fact]
    public async Task Deleting_or_running_a_number_that_does_not_exist_is_exit_2()
    {
        using var test = new ScreenHarness();

        Assert.Equal(ExitCodes.Usage, await test.RunAsync("query", "delete", "9"));
        Assert.Equal(ExitCodes.Usage, await test.RunAsync("command", "run", "9", "--dry-run", "--intent", ScreenHarness.Intent));
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_run_naming_nothing_without_a_terminal_asks_for_a_number_and_calls_nothing()
    {
        using var test = new ScreenHarness();

        var query = await test.RunAsync("query", "run", "--intent", ScreenHarness.Intent);
        var screen = await test.RunAsync("command", "run", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Usage, query);
        Assert.Equal(ExitCodes.Usage, screen);
        Assert.Contains("velixo command list", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task At_a_terminal_a_run_picks_from_the_list_and_asks_again_for_a_required_value()
    {
        using var test = new ScreenHarness(terminal: true, input: "\n2026-09-30\n");
        test.Mcp.ThenText(ScreenHarness.Done(Plan)).ThenText(ScreenHarness.Done(OtherPlan)).ThenText(ScreenHarness.Done(NoRows));
        await test.RunAsync("query", "build", "AR", "--intent", ScreenHarness.Intent);
        await test.RunAsync("query", "build", "AP", "--intent", ScreenHarness.Intent);
        test.Console.Keys.Enqueue(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        test.Console.Keys.Enqueue(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        var exit = await test.RunAsync("query", "run", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        JsonObject sent = test.Mcp.Calls[2].Arguments;
        Assert.Contains("FROM A SELECT X", (string?)sent["giql"], StringComparison.Ordinal);
        Assert.Equal("2026-09-30", (string?)sent["parameters"]!["As Of Date"]);
        Assert.Equal(2, test.Console.StdErr.Split("As Of Date: ").Length - 1);
    }

    [Fact]
    public async Task At_a_terminal_a_master_detail_run_asks_for_the_header_then_a_line_at_a_time()
    {
        var data = JsonNode.Parse(ScreenHarness.OrderBatch)!["result"]!["data"]!.ToJsonString();
        using var test = new ScreenHarness(terminal: true, input: "ACME\n-\n\nA\n2\ny\nB\n1\n\n");
        test.Mcp.ThenText(ScreenHarness.Done(data));
        await test.RunAsync("command", "build", "Create a sales order for {{CustomerID}}", "--intent", ScreenHarness.Intent);
        test.Console.OutWriter.GetStringBuilder().Clear();

        var exit = await test.RunAsync("command", "run", "1", "--dry-run", "--output", "json", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        JsonNode summary = test.Output;
        Assert.Equal("ACME", (string?)summary["variables"]!["CustomerID"]);
        Assert.True(summary["variables"]!.AsObject().ContainsKey("Note"));
        Assert.Null(summary["variables"]!["Note"]);
        Assert.Equal(2, (int)summary["rows"]!);
        Assert.Contains("CustomerID (string, The customer): ", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("Line 2:", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Single(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_named_build_runs_and_deletes_by_its_name_whatever_its_case()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText(ScreenHarness.Done(Plan)).ThenText(ScreenHarness.Done(NoRows));
        await test.RunAsync("query", "build", "AR balance as of {{AsOfDate}}", "--name", "ar-aging", "--connection", "gab-dev", "--intent", ScreenHarness.Intent);
        Assert.Equal("ar-aging", (string?)test.Output["libraryName"]);
        Assert.Contains("velixo query run ar-aging", test.Console.StdErr, StringComparison.Ordinal);

        var run = await test.RunAsync("query", "run", "AR-Aging", "--param", "As Of Date=2026-09-30", "--intent", ScreenHarness.Intent);
        test.Console.OutWriter.GetStringBuilder().Clear();
        await test.RunAsync("query", "list");
        var listedName = (string?)test.Output.AsArray()[0]!["name"];
        var deleted = await test.RunAsync("query", "delete", "ar-aging");

        Assert.Equal(ExitCodes.Success, run);
        Assert.Equal("gab-dev", (string?)test.Mcp.Calls[1].Arguments["connectionName"]);
        Assert.Equal("ar-aging", listedName);
        Assert.Equal(ExitCodes.Success, deleted);
        Assert.Empty(test.Profiles.LoadLibrary("prod").Queries);
    }

    [Fact]
    public async Task A_name_used_again_moves_to_the_newest_build_and_the_old_entry_keeps_its_number()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText(ScreenHarness.Done(Plan)).ThenText(ScreenHarness.Done(OtherPlan));
        await test.RunAsync("query", "build", "AR", "--name", "balances", "--intent", ScreenHarness.Intent);

        await test.RunAsync("query", "build", "AP", "--name", "balances", "--intent", ScreenHarness.Intent);

        List<LibraryEntry> queries = test.Profiles.LoadLibrary("prod").Queries;
        Assert.Null(queries[0].Name);
        Assert.Equal("balances", queries[1].Name);
        Assert.Contains("The name 'balances' moved from query 1 to this one", test.Console.StdErr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ar aging")]
    [InlineData("3")]
    [InlineData("-ar")]
    [InlineData("ar/aging")]
    public async Task A_name_that_is_not_a_key_is_refused_before_the_build_calls_anything(string name)
    {
        using var test = new ScreenHarness();

        var query = await test.RunAsync("query", "build", "AR", "--name", name, "--intent", ScreenHarness.Intent);
        var screen = await test.RunAsync("command", "build", "Read a customer", "--name", name, "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Usage, query);
        Assert.Equal(ExitCodes.Usage, screen);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_named_batch_shows_its_name_in_the_run_commands()
    {
        using var test = new ScreenHarness(terminal: true);
        test.Mcp.ThenText(ScreenHarness.Done("""{"target":"AR303000","commands":[{"type":"Set","name":"Customer.CustomerID","value":"{{CustomerID}}"},{"type":"Get","name":"Customer.Name"}]}"""));

        await test.RunAsync("command", "build", "Read a customer's name", "--name", "customer-name", "--intent", ScreenHarness.Intent);

        Assert.Contains("  velixo command run customer-name --param CustomerID=...", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Screen_delete_at_a_terminal_picks_the_batch()
    {
        using var test = new ScreenHarness(terminal: true);
        test.Mcp.ThenText(ScreenHarness.Done("""{"target":"AR303000","commands":[{"type":"Get","name":"Customer.Name"}]}"""));
        await test.RunAsync("command", "build", "Read a customer's name", "--intent", ScreenHarness.Intent);
        test.Console.Keys.Enqueue(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        var exit = await test.RunAsync("command", "delete");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(test.Profiles.LoadLibrary("prod").Screens);
    }

    private const string LibraryCommandsNumberKey = "libraryNumber";
}
