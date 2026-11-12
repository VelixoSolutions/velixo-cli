using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using static Velixo.Cli.IntegrationTests.LiveHelpers;

namespace Velixo.Cli.IntegrationTests;

/// <summary>
/// <c>query run</c> and <c>query workbook</c> on each ERP, from the checked-in queries and plans under
/// <c>Fixtures/&lt;erp&gt;/</c>, so they call no builder:
/// <list type="bullet">
/// <item><c>customers.vql</c>: every customer, ID then name, ordered by ID, with no parameters.</item>
/// <item><c>gl-lines.vql</c>: the posted GL lines between the required labels <c>From Date</c> and <c>To Date</c>, ordered.</item>
/// <item><c>plan.json</c>: both queries as a plan of two items, the GL lines second.</item>
/// </list>
/// The builder's own tests are in <see cref="QueryBuildTests"/>.
/// </summary>
public sealed class QueryTests
{
    /// <summary>A month of GL activity on each ERP's test company, for the <c>From Date</c> and <c>To Date</c> of <c>gl-lines.vql</c>.</summary>
    public static (string From, string To) GlMonth(Erp erp) => erp switch
    {
        Erp.Acumatica => ("2024-01-01", "2024-01-31"),
        Erp.SageIntacct => ("2018-01-01", "2018-01-31"),
        Erp.BusinessCentral => ("2023-01-01", "2023-01-31"),
        _ => throw new ArgumentOutOfRangeException(nameof(erp), erp, null),
    };

    [LiveTheory]
    [InlineData(Erp.Acumatica)]
    [InlineData(Erp.SageIntacct)]
    [InlineData(Erp.BusinessCentral)]
    [Covers("query run")]
    [CoversFlags("query run", "--query", "--top", "--skip")]
    [CoversFlags(CoversFlagsAttribute.Shared, "--intent")]
    public async Task A_query_file_runs_in_windows_and_from_stdin(Erp erp)
    {
        var query = LiveCli.Fixture(erp, "customers.vql");
        var connection = LiveCli.ConnectionOf(erp);

        JsonArray first = Rows(
            (await LiveCli.RunAsync("query", "run", "--query", query, "--top", "3", "--connection", connection, "--intent", LiveCli.Intent)).Expect());
        Assert.Equal(3, first.Count);

        JsonArray skipped = Rows((await LiveCli.RunAsync("query", "run", "--query", query, "--skip", "1", "--top", "2", "--connection", connection)).Expect());
        Assert.Equal(first[1]!.ToJsonString(), skipped[0]!.ToJsonString());
        Assert.Equal(first[2]!.ToJsonString(), skipped[1]!.ToJsonString());

        JsonArray piped = Rows(
            (await LiveCli.RunWithInputAsync(await File.ReadAllTextAsync(query), "query", "run", "--query", "-", "--top", "1", "--connection", connection)).Expect());
        Assert.Equal(first[0]!.ToJsonString(), Assert.Single(piped)!.ToJsonString());
    }

    [LiveTheory]
    [InlineData(Erp.Acumatica)]
    [InlineData(Erp.SageIntacct)]
    [InlineData(Erp.BusinessCentral)]
    [Covers("query run")]
    [CoversFlags("query run", "--all", "--max-rows")]
    public async Task All_reads_window_after_window_until_max_rows_and_says_it_truncated(Erp erp)
    {
        CliRun run = (await LiveCli.RunAsync(
            "query", "run", "--query", LiveCli.Fixture(erp, "customers.vql"), "--all", "--top", "2", "--max-rows", "5",
            "--connection", LiveCli.ConnectionOf(erp))).Expect();

        JsonObject data = QueryData(run);
        Assert.Equal(5, data["rows"]!.AsArray().Count);
        Assert.True((bool?)data["truncated"], run.ToString());
        Assert.Contains("--max-rows", (string?)data["warning"], StringComparison.Ordinal);
        Assert.Contains("--max-rows", run.StdErr, StringComparison.Ordinal);
    }

    [LiveTheory]
    [InlineData(Erp.Acumatica)]
    [InlineData(Erp.SageIntacct)]
    [InlineData(Erp.BusinessCentral)]
    [Covers("query run")]
    [CoversFlags("query run", "--param", "--params")]
    public async Task Parameters_bind_by_label_and_the_declarations_warn_of_unknown_and_missing_ones(Erp erp)
    {
        var query = LiveCli.Fixture(erp, "gl-lines.vql");
        var connection = LiveCli.ConnectionOf(erp);
        (string from, string to) = GlMonth(erp);
        var paramsFile = LiveCli.TempFile($"gl-lines.{LiveCli.FolderOf(erp)}.params.json");
        await File.WriteAllTextAsync(paramsFile, new JsonObject { ["From Date"] = "1900-01-01", ["To Date"] = to }.ToJsonString());

        // --param wins over the same key in --params.
        CliRun bound = (await LiveCli.RunAsync(
            "query", "run", "--query", query, "--params", paramsFile, "--param", $"From Date={from}", "--top", "5", "--connection", connection)).Expect();
        JsonArray rows = Rows(bound);
        Assert.NotEmpty(rows);
        Assert.DoesNotContain("From Date", bound.StdErr, StringComparison.Ordinal);
        Assert.All(rows, row => Assert.StartsWith(from[..7], DateOf(row), StringComparison.Ordinal));

        // A key no DECLARE takes, and a REQUIRED parameter without a value: one warning for each, before the run.
        CliRun warned = await LiveCli.RunAsync(
            "query", "run", "--query", query, "--param", $"From Date={from}", "--param", "Velixo IT Unknown=1", "--top", "1", "--connection", connection);
        Assert.Contains("'Velixo IT Unknown'", warned.StdErr, StringComparison.Ordinal);
        Assert.Contains("To Date", warned.StdErr, StringComparison.Ordinal);
    }

    [LiveTheory]
    [InlineData(Erp.Acumatica)]
    [InlineData(Erp.SageIntacct)]
    [InlineData(Erp.BusinessCentral)]
    [Covers("query run")]
    public async Task Rows_print_as_csv_and_as_a_table_with_the_window_in_its_footer(Erp erp)
    {
        var query = LiveCli.Fixture(erp, "customers.vql");
        var connection = LiveCli.ConnectionOf(erp);
        CliRun json = (await LiveCli.RunAsync("query", "run", "--query", query, "--top", "3", "--connection", connection)).Expect();
        var captions = QueryData(json)["columns"]!.AsArray().Select(column => (string)column!["caption"]!).ToList();

        CliRun csv = (await LiveCli.RunAsync("query", "run", "--query", query, "--top", "3", "--connection", connection, "--output", "csv")).Expect();
        var lines = csv.StdOut.TrimEnd().Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.Equal(string.Join(',', captions.Select(Csv)), lines[0].TrimEnd('\r'));

        CliRun table = (await LiveCli.RunAsync("query", "run", "--query", query, "--top", "3", "--connection", connection, "--output", "table")).Expect();
        Assert.Contains(captions[0], table.StdOut, StringComparison.Ordinal);
        Assert.Contains("rows 1 to 3", table.StdOut, StringComparison.Ordinal);
    }

    [LiveTheory]
    [InlineData(Erp.Acumatica)]
    [InlineData(Erp.SageIntacct)]
    [InlineData(Erp.BusinessCentral)]
    [Covers("query run")]
    [CoversFlags("query run", "--plan")]
    public async Task A_plan_of_two_queries_runs_each_item_and_gives_each_only_its_own_parameters(Erp erp)
    {
        var plan = LiveCli.Fixture(erp, "plan.json");
        (string from, string to) = GlMonth(erp);
        string[] args = ["query", "run", "--plan", plan, "--param", $"From Date={from}", "--param", $"To Date={to}", "--top", "2", "--connection", LiveCli.ConnectionOf(erp)];

        CliRun run = (await LiveCli.RunAsync(args)).Expect();
        JsonArray results = run.Json!["results"]!.AsArray();
        Assert.Equal(2, results.Count);
        Assert.All(results, item => Assert.NotEmpty(item!["result"]!["result"]!["data"]!["rows"]!.AsArray()));

        // Two results cannot share one CSV header. A CSV run reports the error as text on stderr.
        CliRun csv = (await LiveCli.RunAsync([.. args, "--output", "csv"])).Expect(ExitCodes.Usage);
        Assert.Contains($"[{ErrorCodes.Usage}]", csv.StdErr, StringComparison.Ordinal);
    }

    [LiveTheory]
    [InlineData(Erp.Acumatica)]
    [InlineData(Erp.SageIntacct)]
    [InlineData(Erp.BusinessCentral)]
    [Covers("query workbook", "files download")]
    [CoversFlags("query workbook", "--plan", "--item", "--param", "--params", "--download", "--query")]
    [CoversFlags("files download", "--out")]
    public async Task A_query_becomes_a_workbook_that_downloads(Erp erp)
    {
        var connection = LiveCli.ConnectionOf(erp);
        var directory = Directory.CreateTempSubdirectory("velixo-it-workbooks-").FullName;
        (string from, string to) = GlMonth(erp);
        var paramsFile = LiveCli.TempFile($"workbook.{LiveCli.FolderOf(erp)}.params.json");
        await File.WriteAllTextAsync(paramsFile, new JsonObject { ["To Date"] = to }.ToJsonString());

        // The plan's second item, the GL lines, downloaded by the command itself into a directory.
        CliRun planned = (await LiveCli.RunAsync(
            "query", "workbook", "--plan", LiveCli.Fixture(erp, "plan.json"), "--item", "2", "--param", $"From Date={from}", "--params", paramsFile,
            "--download", directory, "--connection", connection)).Expect();
        JsonNode attachment = planned.Json!["result"]!["data"]!["attachments"]![0]!;
        var path = (string?)attachment["path"];
        Assert.True(path is not null && File.Exists(path) && new FileInfo(path).Length > 0, planned.ToString());
        Assert.Equal(directory, Path.GetDirectoryName(path));

        // A query file, its link fetched by files download to an exact path.
        CliRun workbook = (await LiveCli.RunAsync("query", "workbook", "--query", LiveCli.Fixture(erp, "customers.vql"), "--connection", connection)).Expect();
        var url = (string?)workbook.Json!["result"]!["data"]!["attachments"]![0]!["downloadUrl"];
        Assert.False(string.IsNullOrEmpty(url), workbook.ToString());
        var target = Path.Combine(directory, "customers.xlsx");

        CliRun download = (await LiveCli.RunAsync("files", "download", url!, "--out", target)).Expect();
        Assert.Equal(target, (string?)download.Json![0]!["path"]);
        Assert.True(new FileInfo(target).Length > 0, download.ToString());
    }

    [LiveFact]
    [Covers("query run")]
    [CoversFlags("query run", "--inquiry")]
    public async Task A_saved_inquiry_runs_by_its_name()
    {
        CliRun run = (await LiveCli.RunAsync("query", "run", "--inquiry", AcumaticaInquiry, "--top", "3", "--connection", LiveCli.Connection)).Expect();

        Assert.InRange(Rows(run).Count, 1, 3);
    }

    [LiveFact]
    [Covers("query run")]
    [CoversFlags(CoversFlagsAttribute.Shared, "--timeout")]
    public async Task Timeout_stops_a_run_that_reads_window_after_window()
    {
        // One row per call: the customers take far more calls than a one-second budget holds.
        CliRun run = await LiveCli.RunAsync(
            "query", "run", "--query", LiveCli.Fixture(Erp.Acumatica, "customers.vql"), "--all", "--top", "1", "--timeout", "1", "--connection", LiveCli.Connection);

        run.ExpectError(ExitCodes.Timeout, ErrorCodes.Timeout);
    }

    /// <summary>A generic inquiry on the Acumatica test company that needs no parameter.</summary>
    private const string AcumaticaInquiry = "AR-Customers";

    /// <summary>The posting date of a <c>gl-lines.vql</c> row, its first column, as <c>YYYY-MM-DD…</c>.</summary>
    private static string DateOf(JsonNode? row) => (string?)row?[0] ?? string.Empty;
}
