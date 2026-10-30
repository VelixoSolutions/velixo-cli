using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class QueryCommandsTests : IDisposable
{
    private const string Intent = "Find the overdue invoices to chase before month-end";

    private const string Running = """{"taskId":"t_1","status":"running","progress":{"label":"Building the query","elapsedMs":12000,"stalledForMs":0}}""";

    private const string OrderedQuery = """
        DECLARE [AsOf] SCHEMA [PX.Objects.AR.ARInvoice].DocDate AS 'As Of Date' REQUIRED
        FROM ARInvoice
        ORDER BY ARInvoice.CustomerID
        SELECT CustomerID, Balance
        """;

    private readonly TestServices _test = new();
    private readonly FakeMcpClient _mcp = new();

    public QueryCommandsTests() => _test.Services.McpClientFactory = _ => _mcp;

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task Build_sends_the_request_and_the_base_queries_and_prints_the_plan_with_query_keys()
    {
        var first = WriteFile("a.vql", "FROM A SELECT X\n");
        var second = WriteFile("b.vql", "FROM B SELECT Y");
        _mcp.ThenText(Running).ThenText(Done("""{"queries":[{"purpose":"Balance","giql":"FROM A SELECT X"}],"note":"Pass 'As Of Date'."}"""));

        var exit = await Run("query", "build", "AR balance as of {{AsOfDate}}", "--base-query", first, "--base-query", second, "--connection", "gab-dev");

        Assert.Equal(ExitCodes.Success, exit);
        JsonObject arguments = _mcp.Calls[0].Arguments;
        Assert.Equal(McpToolNames.PrepareQuery, _mcp.Calls[0].Name);
        Assert.Equal(Intent, (string?)arguments["intent"]);
        Assert.Equal("AR balance as of {{AsOfDate}}", (string?)arguments["request"]);
        Assert.Equal("FROM A SELECT X\n\nFROM B SELECT Y", (string?)arguments["baseQuery"]);
        Assert.Equal("gab-dev", (string?)arguments["connectionName"]);
        Assert.Equal(McpToolNames.GetStatus, _mcp.Calls[1].Name);

        JsonNode item = Output()["result"]!["data"]!["queries"]![0]!;
        Assert.Equal("FROM A SELECT X", (string?)item["query"]);
        Assert.Null(item["giql"]);
        Assert.Contains("Building the query", _test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_on_a_terminal_prints_the_whole_query_its_parameters_the_note_and_how_to_run_it()
    {
        var plan = new JsonObject
        {
            ["queries"] = new JsonArray(new JsonObject { ["purpose"] = "Balance per customer", ["giql"] = OrderedQuery }),
            ["note"] = "Pass 'As Of Date' as YYYY-MM-DD.",
        };
        _mcp.ThenText(Done(plan.ToJsonString()));

        var exit = await Run("query", "build", "AR balance as of {{AsOfDate}}", "--output", "table");

        Assert.Equal(ExitCodes.Success, exit);
        var printed = _test.Console.StdOut;
        Assert.StartsWith("Balance per customer\n", printed, StringComparison.Ordinal);
        Assert.Contains("  SELECT CustomerID, Balance\n", printed, StringComparison.Ordinal);
        Assert.Contains("  As Of Date  required\n", printed, StringComparison.Ordinal);
        Assert.Contains("  Pass 'As Of Date' as YYYY-MM-DD.\n", printed, StringComparison.Ordinal);
        Assert.Contains("  velixo query run 1    # asks for each value\n", printed, StringComparison.Ordinal);
        Assert.Contains("  velixo query run 1 --param \"As Of Date=...\"\n", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("--plan", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("…", printed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_with_save_writes_the_plan_and_names_the_file_in_the_run_command()
    {
        _mcp.ThenText(Done("""{"queries":[{"purpose":"Balance","giql":"DECLARE [AsOf] SCHEMA [PX.Objects.AR.ARInvoice].DocDate AS 'As Of Date' REQUIRED\nFROM A SELECT X"}]}"""));

        var exit = await Run("query", "build", "AR balance as of {{AsOfDate}}", "--save", "ar.plan.json", "--output", "table");

        Assert.Equal(ExitCodes.Success, exit);
        JsonNode saved = JsonNode.Parse(File.ReadAllText(Path.Combine(_test.Directory, "ar.plan.json")))!;
        Assert.StartsWith("DECLARE [AsOf]", (string?)saved["result"]!["data"]!["queries"]![0]!["query"], StringComparison.Ordinal);
        Assert.Contains("velixo query run --plan ar.plan.json --param \"As Of Date=...\"", _test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_with_a_forced_connection_repeats_it_in_the_run_command()
    {
        _mcp.ThenText(Done("""{"queries":[{"purpose":"Balance","giql":"FROM A SELECT X"}]}"""));

        await Run("query", "build", "AR balance", "--save", "ar.plan.json", "--connection", "gab dev", "--output", "table");

        Assert.Contains("  velixo query run --plan ar.plan.json --connection \"gab dev\"\n", _test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_with_save_to_a_vql_file_writes_the_query_text_and_runs_it_with_query()
    {
        _mcp.ThenText(Done("""{"queries":[{"purpose":"Balance","giql":"DECLARE [AsOf] SCHEMA [PX.Objects.AR.ARInvoice].DocDate AS 'As Of Date' REQUIRED\nFROM A SELECT X"}]}"""));

        var exit = await Run("query", "build", "AR balance as of {{AsOfDate}}", "--save", "ar.vql", "--connection", "gab-dev", "--output", "table");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("DECLARE [AsOf] SCHEMA [PX.Objects.AR.ARInvoice].DocDate AS 'As Of Date' REQUIRED\nFROM A SELECT X\n", File.ReadAllText(Path.Combine(_test.Directory, "ar.vql")));
        var printed = _test.Console.StdOut;
        Assert.Contains("  velixo query run --query ar.vql --param \"As Of Date=...\" --connection gab-dev\n", printed, StringComparison.Ordinal);
        Assert.Contains("\nvelixo query run --query - --param \"As Of Date=...\" --connection gab-dev <<'VQL'\nDECLARE [AsOf]", printed, StringComparison.Ordinal);
        Assert.EndsWith("FROM A SELECT X\nVQL\n", printed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_with_save_to_a_vql_file_refuses_a_plan_of_two_queries_and_saves_nothing()
    {
        _mcp.ThenText(Done("""{"queries":[{"purpose":"A","giql":"FROM A SELECT X"},{"purpose":"B","giql":"FROM B SELECT Y"}]}"""));

        var exit = await Run("query", "build", "Actual and budget", "--save", "ab.vql", "--output", "table");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(File.Exists(Path.Combine(_test.Directory, "ab.vql")));
        Assert.Contains("Not saved: a .vql file holds one query, and this plan has 2 items.", _test.Console.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("Or inline", _test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_on_windows_prints_the_inline_form_for_powershell()
    {
        _test.Environment.IsWindows = true;
        _mcp.ThenText(Done("""{"queries":[{"purpose":"Balance","giql":"FROM A SELECT X"}]}"""));

        await Run("query", "build", "AR balance", "--connection", "gab-dev", "--output", "table");

        Assert.EndsWith("Or inline:\n@'\nFROM A SELECT X\n'@ | velixo query run --query - --connection gab-dev\n", _test.Console.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("<<", _test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_with_save_in_json_still_prints_the_plan()
    {
        _mcp.ThenText(Done("""{"queries":[{"purpose":"Balance","giql":"FROM A SELECT X"}]}"""));

        var exit = await Run("query", "build", "AR balance", "--save", "ar.plan.json");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("FROM A SELECT X", (string?)Output()["result"]!["data"]!["queries"]![0]!["query"]);
        Assert.True(File.Exists(Path.Combine(_test.Directory, "ar.plan.json")));
    }

    [Fact]
    public void Amounts_read_with_separators_and_the_decimals_their_column_needs()
    {
        var columns = JsonNode.Parse("""
            [{"caption":"Customer","type":"String"},{"caption":"Total","type":"Decimal","precision":4},
             {"caption":"Rate","type":"Decimal","precision":4},{"caption":"ID","type":"Int32"}]
            """)!.AsArray();
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["ACME", "1231705.0", "0.125", "1234"],
            ["BCORP", "74998.59", "", "7"],
        ];

        (IReadOnlyList<IReadOnlyList<string>> read, IReadOnlyList<bool> right) = Velixo.Cli.Query.QueryOutput.ForReading(columns, rows);

        Assert.Equal(["ACME", "1,231,705.00", "0.125", "1234"], read[0]);
        Assert.Equal(["BCORP", "74,998.59", "", "7"], read[1]);
        Assert.Equal([false, true, true, true], right);
    }

    [Fact]
    public async Task A_partial_table_shows_the_footer_not_the_servers_agent_warning_and_json_keeps_it()
    {
        JsonObject data = JsonNode.Parse(QueryResult(2, 66))!.AsObject();
        data["truncated"] = true;
        data["warning"] = "WARNING: this result is PARTIAL. 66 rows matched and rows 1-2 were returned. Do NOT analyse...";
        var query = WriteFile("q.vql", "FROM X SELECT Y");
        _mcp.ThenText(Done(data.ToJsonString())).ThenText(Done(data.ToJsonString()));

        await Run("query", "run", "--query", query, "--top", "2", "--output", "table");

        Assert.DoesNotContain("PARTIAL", _test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("rows 1 to 2 of 66; add --skip 2 or --all for the rest", _test.Console.StdOut, StringComparison.Ordinal);
        Assert.Contains("  100.50", _test.Console.StdOut, StringComparison.Ordinal);

        await Run("query", "run", "--query", query, "--top", "2");

        Assert.Contains("PARTIAL", _test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plan_run_in_a_table_does_not_repeat_the_note()
    {
        var plan = WriteFile("p.plan.json", """{"queries":[{"purpose":"Balance","query":"FROM X SELECT Y"}],"note":"Join on CustomerID."}""");
        _mcp.ThenText(Done(QueryResult(1, 1)));

        var exit = await Run("query", "run", "--plan", plan, "--output", "table");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("Join on CustomerID.", _test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plan_of_one_query_prints_what_the_same_query_from_a_file_prints()
    {
        var plan = WriteFile("p.plan.json", """{"queries":[{"purpose":"Balance","query":"FROM X SELECT Y"}],"note":"Join on CustomerID."}""");
        var query = WriteFile("p.vql", "FROM X SELECT Y");
        _mcp.ThenText(Done(QueryResult(2, 2))).ThenText(Done(QueryResult(2, 2)));

        await Run("query", "run", "--plan", plan);
        var fromPlan = Output().ToJsonString();
        _test.Console.OutWriter.GetStringBuilder().Clear();
        await Run("query", "run", "--query", query);

        Assert.Equal(Output().ToJsonString(), fromPlan);
        Assert.Equal(2, (int?)Output()["result"]!["data"]!["totalCount"]);
    }

    [Fact]
    public async Task Run_without_an_intent_sends_the_command_path_as_the_intent()
    {
        var query = WriteFile("q.vql", "FROM X SELECT Y");
        _mcp.ThenText(Done(QueryResult(1, 1)));

        var exit = await _test.RunAsync("query", "run", "--query", query, "--param", "Customer=ACME");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("Run from the Velixo CLI: velixo query run", (string?)_mcp.Calls[0].Arguments["intent"]);
    }

    [Fact]
    public async Task The_intent_flag_wins_over_the_variable_and_the_default()
    {
        _test.Environment.Variables["VELIXO_INTENT"] = "Intent from the variable";
        var query = WriteFile("q.vql", "FROM X SELECT Y");
        _mcp.ThenText(Done(QueryResult(1, 1)));

        var exit = await Run("query", "run", "--query", query);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(Intent, (string?)_mcp.Calls[0].Arguments["intent"]);
    }

    [Fact]
    public async Task The_intent_variable_wins_over_the_default()
    {
        _test.Environment.Variables["VELIXO_INTENT"] = "Intent from the variable";
        var query = WriteFile("q.vql", "FROM X SELECT Y");
        _mcp.ThenText(Done(QueryResult(1, 1)));

        var exit = await _test.RunAsync("query", "run", "--query", query);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("Intent from the variable", (string?)_mcp.Calls[0].Arguments["intent"]);
    }

    [Fact]
    public async Task A_refusal_the_server_reports_as_a_failed_build_is_exit_3()
    {
        _mcp.ThenText("""{"taskId":"t_1","status":"failed","error":"ERROR: Which inquiry do you mean: AR-Aging or AR-Open?"}""", isError: true);

        var exit = await Run("query", "build", "the AR aging GI");

        Assert.Equal(ExitCodes.BuilderRefusal, exit);
        JsonNode error = Output()["error"]!;
        Assert.Equal(ErrorCodes.BuilderRefusal, (string?)error["code"]);
        Assert.StartsWith("ERROR: Which inquiry", (string?)error["message"], StringComparison.Ordinal);
        Assert.Equal("t_1", (string?)error["taskId"]);
    }

    [Fact]
    public async Task A_refusal_as_the_text_of_a_done_build_is_exit_3()
    {
        _mcp.ThenText(Done("ERROR: No fiscal period was given."));

        var exit = await Run("query", "build", "GL balance");

        Assert.Equal(ExitCodes.BuilderRefusal, exit);
        Assert.Equal(ErrorCodes.BuilderRefusal, (string?)Output()["error"]!["code"]);
    }

    [Fact]
    public async Task Build_with_no_wait_prints_the_running_envelope()
    {
        _mcp.ThenText(Running);

        var exit = await Run("query", "build", "AR balance", "--no-wait");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(_mcp.Calls);
        Assert.Equal("running", (string?)Output()["status"]);
        Assert.Equal("t_1", (string?)Output()["taskId"]);
    }

    [Fact]
    public async Task Build_past_its_timeout_is_exit_124_with_the_task_id()
    {
        _mcp.ThenText(Running).ThenHang();

        var exit = await Run("query", "build", "AR balance", "--timeout", "1");

        Assert.Equal(ExitCodes.Timeout, exit);
        Assert.Equal("t_1", (string?)Output()["error"]!["taskId"]);
    }

    [Fact]
    public async Task Run_plan_runs_every_item_in_order_and_prints_the_note_and_each_result()
    {
        var plan = WriteFile("ar.plan.json", $$"""
            {"taskId":"t_0","status":"done","result":{"data":{"queries":[
              {"purpose":"Balance per customer","query":{{JsonString(OrderedQuery)}} },
              {"purpose":"Customer names","giql":"DECLARE [Br] TYPE string AS 'Branch' FROM Customer SELECT AcctName"}
            ],"note":"Join on CustomerID."} } }
            """);
        _mcp.ThenText(Done(QueryResult(2, 2))).ThenText(Done(QueryResult(1, 1)));

        var exit = await Run("query", "run", "--plan", plan, "--param", "As Of Date=2026-09-30", "--param", "Branch=HQ");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([McpToolNames.ExecuteQuery, McpToolNames.ExecuteQuery], _mcp.Calls.Select(call => call.Name));
        Assert.Equal(OrderedQuery, (string?)_mcp.Calls[0].Arguments["giql"]);
        Assert.Equal("""{"As Of Date":"2026-09-30"}""", _mcp.Calls[0].Arguments["parameters"]!.ToJsonString());
        Assert.Equal("""{"Branch":"HQ"}""", _mcp.Calls[1].Arguments["parameters"]!.ToJsonString());
        Assert.Equal(250, (int?)_mcp.Calls[0].Arguments["top"]);
        Assert.Equal(0, (int?)_mcp.Calls[0].Arguments["skip"]);

        JsonObject output = Output().AsObject();
        Assert.Equal(["note", "results"], output.Select(pair => pair.Key));
        Assert.Equal("Join on CustomerID.", (string?)output["note"]);
        JsonArray results = output["results"]!.AsArray();
        Assert.Equal(["Balance per customer", "Customer names"], results.Select(result => (string?)result!["purpose"]));
        Assert.Equal(2, (int?)results[0]!["result"]!["result"]!["data"]!["totalCount"]);
        Assert.DoesNotContain("warning", _test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_plan_runs_an_inquiry_item_with_its_placeholders_filled()
    {
        var plan = WriteFile("gi.plan.json", """
            {"queries":[{"purpose":"Aging","inquiry":{"name":"AR-Aging","parameters":{"Branch":"{{BranchID}}"}}}],"note":null}
            """);
        _mcp.ThenText(Done(QueryResult(1, 1)));

        var exit = await Run("query", "run", "--plan", plan, "--param", "BranchID=HQ");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("AR-Aging", (string?)_mcp.Calls[0].Arguments["inquiry"]);
        Assert.Null(_mcp.Calls[0].Arguments["giql"]);
        Assert.Equal("""{"Branch":"HQ"}""", _mcp.Calls[0].Arguments["parameters"]!.ToJsonString());
    }

    [Fact]
    public async Task Run_merges_params_file_and_param_flags_with_the_flags_winning()
    {
        var query = WriteFile("q.vql", "DECLARE [A] TYPE string AS 'A' DECLARE [B] TYPE string AS 'B' FROM X SELECT Y");
        var parameters = WriteFile("p.json", """{"A":"1","B":"2"}""");
        _mcp.ThenText(Done(QueryResult(1, 1)));

        var exit = await Run("query", "run", "--query", query, "--params", parameters, "--param", "B=3=x", "--top", "10", "--skip", "5");

        Assert.Equal(ExitCodes.Success, exit);
        JsonObject arguments = _mcp.Calls[0].Arguments;
        Assert.Equal("""{"A":"1","B":"3=x"}""", arguments["parameters"]!.ToJsonString());
        Assert.Equal(10, (int?)arguments["top"]);
        Assert.Equal(5, (int?)arguments["skip"]);
    }

    [Fact]
    public async Task Run_reads_relative_paths_against_the_working_directory()
    {
        WriteFile("q.vql", OrderedQuery);
        WriteFile("p.json", """{"As Of Date":"2026-09-30"}""");
        _mcp.ThenText(Done(QueryResult(0, 0)));

        var exit = await Run("query", "run", "--query", "q.vql", "--params", "p.json");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(OrderedQuery, (string?)_mcp.Calls[0].Arguments["giql"]);
        Assert.Equal("2026-09-30", (string?)_mcp.Calls[0].Arguments["parameters"]!["As Of Date"]);
    }

    [Fact]
    public async Task Run_warns_about_an_unknown_label_and_a_missing_required_value()
    {
        var query = WriteFile("q.vql", """
            DECLARE [AsOf] SCHEMA [PX.Objects.AR.ARInvoice].DocDate AS 'As Of Date' REQUIRED
            DECLARE [Branch] TYPE string REQUIRED
            FROM ARInvoice SELECT CustomerID
            """);
        _mcp.ThenText(Done(QueryResult(0, 0)));

        var exit = await Run("query", "run", "--query", query, "--param", "AsOf=2026-09-30");

        Assert.Equal(ExitCodes.Success, exit);
        var stderr = _test.Console.StdErr;
        Assert.Contains("The parameter 'AsOf' matches no parameter the query takes ('As Of Date', 'Branch')", stderr, StringComparison.Ordinal);
        Assert.Contains("The query requires 'As Of Date'", stderr, StringComparison.Ordinal);
        Assert.Contains("The query requires 'Branch'", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_all_pages_until_a_short_page_and_merges_the_rows()
    {
        var query = WriteFile("q.vql", OrderedQuery);
        _mcp.ThenText(Done(QueryResult(2, 5, firstRow: 1)))
            .ThenText(Done(QueryResult(2, 5, firstRow: 3)))
            .ThenText(Done(QueryResult(1, 5, firstRow: 5)));

        var exit = await Run("query", "run", "--query", query, "--param", "As Of Date=2026-09-30", "--all", "--top", "2");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([0, 2, 4], _mcp.Calls.Select(call => (int)call.Arguments["skip"]!));
        JsonNode data = Output()["result"]!["data"]!;
        Assert.Equal(["C1", "C2", "C3", "C4", "C5"], data["rows"]!.AsArray().Select(row => (string?)row![0]));
        Assert.Equal(5, (int?)data["totalCount"]);
        Assert.Null(data["truncated"]);
        Assert.Null(data["warning"]);
        Assert.DoesNotContain("ORDER BY", _test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_all_stops_at_max_rows_and_says_the_result_is_partial()
    {
        var query = WriteFile("q.vql", "FROM ARInvoice SELECT CustomerID, Balance");
        _mcp.ThenText(Done(QueryResult(2, 9, firstRow: 1))).ThenText(Done(QueryResult(1, 9, firstRow: 3)));

        var exit = await Run("query", "run", "--query", query, "--all", "--top", "2", "--max-rows", "3");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([2, 1], _mcp.Calls.Select(call => (int)call.Arguments["top"]!));
        JsonNode data = Output()["result"]!["data"]!;
        Assert.Equal(3, data["rows"]!.AsArray().Count);
        Assert.True((bool)data["truncated"]!);
        Assert.Contains("--max-rows 3 was reached", (string?)data["warning"], StringComparison.Ordinal);
        Assert.Contains("no ORDER BY", _test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_as_a_table_prints_captions_rows_and_the_window_footer()
    {
        var query = WriteFile("q.vql", "FROM ARInvoice SELECT CustomerID, Balance");
        _mcp.ThenText(Done(QueryResult(2, 1204)));

        var exit = await Run("query", "run", "--query", query, "--output", "table");

        Assert.Equal(ExitCodes.Success, exit);
        var lines = _test.Console.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Customer  Natural Net", lines[0]);
        Assert.StartsWith("C1", lines[2], StringComparison.Ordinal);
        Assert.Equal("rows 1 to 2 of 1,204; add --skip 2 or --all for the rest", lines[^1]);
    }

    [Fact]
    public async Task Run_as_csv_prints_the_captions_then_every_row()
    {
        var query = WriteFile("q.vql", "FROM ARInvoice SELECT CustomerID, Balance");
        _mcp.ThenText(Done(QueryResult(2, 2)));

        var exit = await Run("query", "run", "--query", query, "--output", "csv");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("Customer,Natural Net\nC1,100.5\nC2,200.5\n", _test.Console.StdOut);
    }

    [Fact]
    public async Task A_partial_result_repeats_its_warning_on_stderr()
    {
        var query = WriteFile("q.vql", "FROM ARInvoice SELECT CustomerID, Balance");
        JsonObject data = JsonNode.Parse(QueryResult(1, 40))!.AsObject();
        data["warning"] = "WARNING: this result is PARTIAL.";
        data["truncated"] = true;
        _mcp.ThenText(Done(data.ToJsonString()));

        var exit = await Run("query", "run", "--query", query, "--top", "1");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("warning: WARNING: this result is PARTIAL.", _test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_erp_conversion_error_is_exit_6_with_the_erps_message_in_detail()
    {
        const string erp = "HTTP 400 Bad Request: Conversion failed when converting the nvarchar value 'ABC' to data type int.";
        var query = WriteFile("q.vql", "FROM ARInvoice SELECT CustomerID");
        _mcp.ThenText($$"""{"taskId":"t_9","status":"failed","error":{{JsonString(erp)}}}""", isError: true);

        var exit = await Run("query", "run", "--query", query);

        Assert.Equal(ExitCodes.TaskFailed, exit);
        JsonNode error = Output()["error"]!;
        Assert.Equal(erp, (string?)error["detail"]);
        Assert.Contains("build it again quoting this error", (string?)error["nextStep"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--query", "q.vql", "--inquiry", "AR-Aging")]
    [InlineData("--all", "--no-wait", "--query", "q.vql")]
    [InlineData("--top", "10001", "--query", "q.vql")]
    public async Task Run_refuses_a_bad_combination_before_any_call(params string[] flags)
    {
        WriteFile("q.vql", "FROM X SELECT Y");
        var args = new List<string> { "query", "run" };
        args.AddRange(flags.Select(flag => flag == "q.vql" ? Path.Combine(_test.Directory, flag) : flag));

        var exit = await Run([.. args]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_mcp.Calls);
    }

    [Fact]
    public async Task Workbook_refuses_a_plan_item_that_runs_a_saved_inquiry()
    {
        var plan = WriteFile("gi.plan.json", """{"queries":[{"purpose":"Aging","inquiry":{"name":"AR-Aging","parameters":{}}}]}""");

        var exit = await Run("query", "workbook", "--plan", plan);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("as a workbook", (string?)Output()["error"]!["nextStep"], StringComparison.Ordinal);
        Assert.Empty(_mcp.Calls);
    }

    [Fact]
    public async Task Workbook_sends_the_parameters_as_a_json_string_and_downloads_the_link()
    {
        var query = WriteFile("q.vql", OrderedQuery);
        var target = Path.Combine(_test.Directory, "ar.xlsx");
        _mcp.ThenText("""{"attachments":[{"fileName":"AR.xlsx","downloadUrl":"https://nx-dev.velixo.com/mcp/download/tok","kind":"queryWorkbook"}],"note":"Open it in Excel."}""");
        _test.Http.Then(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("xlsx-bytes")) });

        var exit = await Run("query", "workbook", "--query", query, "--param", "As Of Date=2026-09-30", "--download", target);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(McpToolNames.CreateWorkbookFromQuery, _mcp.Calls[0].Name);
        Assert.Equal(OrderedQuery, (string?)_mcp.Calls[0].Arguments["query"]);
        Assert.Equal("""{"As Of Date":"2026-09-30"}""", (string?)_mcp.Calls[0].Arguments["parameters"]);
        Assert.Equal("https://nx-dev.velixo.com/mcp/download/tok", _test.Http.Requests[0].Uri.ToString());
        Assert.Equal("xlsx-bytes", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.Equal(target, (string?)Output()["result"]!["data"]!["attachments"]![0]!["path"]);
    }

    private static string Done(string textualResponse) =>
        new JsonObject
        {
            ["taskId"] = "t_1",
            ["status"] = "done",
            ["result"] = new JsonObject { ["textualResponse"] = textualResponse },
        }.ToJsonString();

    /// <summary>A query result of <paramref name="rows"/> rows named C1, C2, … from <paramref name="firstRow"/>.</summary>
    private static string QueryResult(int rows, int totalCount, int firstRow = 1)
    {
        var values = new JsonArray();
        for (var index = firstRow; index < firstRow + rows; index++)
        {
            values.Add((JsonNode)new JsonArray($"C{index}", (index * 100) + 0.5));
        }

        return new JsonObject
        {
            ["columns"] = JsonNode.Parse("""
                [{"objectName":"ARInvoice","fieldName":"CustomerID","caption":"Customer","type":"string"},
                 {"objectName":"ARInvoice","fieldName":"Balance","caption":"Natural Net","type":"decimal","precision":2}]
                """),
            ["rows"] = values,
            ["totalCount"] = totalCount,
        }.ToJsonString();
    }

    private static string JsonString(string text) => JsonValue.Create(text).ToJsonString();

    private Task<int> Run(params string[] args) => _test.RunAsync([.. args, "--intent", Intent]);

    private JsonNode Output() => JsonNode.Parse(_test.Console.StdOut)!;

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_test.Directory, name);
        File.WriteAllText(path, content);
        return path;
    }
}
