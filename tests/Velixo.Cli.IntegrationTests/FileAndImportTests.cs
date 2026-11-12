using System.Security.Cryptography;
using System.Text.Json.Nodes;
using static Velixo.Cli.IntegrationTests.LiveHelpers;

namespace Velixo.Cli.IntegrationTests;

/// <summary>
/// <c>files</c> and <c>import</c>. Imports are Acumatica's, so they run on <see cref="LiveCli.Connection"/>,
/// and only with <c>--validate-only</c>: they import nothing.
/// </summary>
public sealed class FileAndImportTests
{
    private const string Scenario = "ACU Import Vendors";

    private const string ScenarioScreen = "AP303000";

    [LiveFact]
    [Covers("files stage")]
    [CoversFlags("files stage", "--no-upload", "--size", "--sha256")]
    public async Task A_file_stages_by_upload_and_by_a_url_that_takes_its_bytes_once()
    {
        var file = LiveCli.TempFile("velixo-it-stage.csv");
        await File.WriteAllTextAsync(file, "Name,Amount\nVelixo CLI test,1\n", TestContext.Current.CancellationToken);
        var bytes = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);

        CliRun uploaded = (await LiveCli.RunAsync("files", "stage", file)).Expect();
        Assert.True((bool?)uploaded.Json!["uploaded"], uploaded.ToString());
        Assert.False(string.IsNullOrEmpty((string?)uploaded.Json!["fileToken"]), uploaded.ToString());

        // The curl route: the URL takes the bytes with a plain PUT, as `curl -T FILE URL` sends them.
        CliRun linked = (await LiveCli.RunAsync("files", "stage", file, "--no-upload")).Expect();
        await PutAsync((string?)linked.Json!["uploadUrl"], bytes);

        // A file that is not on this machine: its name, size and checksum.
        CliRun named = (await LiveCli.RunAsync(
            "files", "stage", "elsewhere.csv", "--size", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--sha256", Convert.ToHexStringLower(SHA256.HashData(bytes)))).Expect();
        Assert.False((bool?)named.Json!["uploaded"], named.ToString());
        Assert.Equal("elsewhere.csv", (string?)named.Json!["fileName"]);
        await PutAsync((string?)named.Json!["uploadUrl"], bytes);
    }

    [LiveFact]
    [Covers("files download")]
    [CoversFlags("files download", "--from", "--dir")]
    public async Task The_links_of_a_saved_result_download_and_never_overwrite()
    {
        var result = LiveCli.TempFile("customers.workbook.json");
        var directory = Directory.CreateTempSubdirectory("velixo-it-downloads-").FullName;
        CliRun workbook = (await LiveCli.RunAsync(
            "query", "workbook", "--query", LiveCli.Fixture(Erp.Acumatica, "customers.vql"), "--connection", LiveCli.Connection)).Expect();
        await File.WriteAllTextAsync(result, workbook.StdOut, TestContext.Current.CancellationToken);

        JsonNode first = (await LiveCli.RunAsync("files", "download", "--from", result, "--dir", directory)).Expect().Json!.AsArray()[0]!;
        JsonNode second = (await LiveCli.RunAsync("files", "download", "--from", result, "--dir", directory)).Expect().Json!.AsArray()[0]!;

        Assert.True(new FileInfo((string)first["path"]!).Length > 0, first.ToJsonString());
        Assert.NotEqual((string?)first["path"], (string?)second["path"]);
        Assert.Contains(" (2)", (string?)second["path"], StringComparison.Ordinal);
    }

    [LiveFact]
    [Covers("import scenarios")]
    [CoversFlags("import scenarios", "--screen", "--filter")]
    public async Task The_import_scenarios_are_listed_by_screen_and_by_name()
    {
        CliRun byScreen = (await LiveCli.RunAsync("import", "scenarios", "--screen", ScenarioScreen, "--connection", LiveCli.Connection)).Expect();
        Assert.NotEmpty(byScreen.Json!.AsArray());
        Assert.All(byScreen.Json!.AsArray(), entry => Assert.Equal(ScenarioScreen, (string?)entry?["screenId"]));

        CliRun byName = (await LiveCli.RunAsync("import", "scenarios", "--filter", Scenario, "--connection", LiveCli.Connection)).Expect();
        Assert.Contains(byName.Json!.AsArray(), entry => (string?)entry?["name"] == Scenario);
    }

    [LiveFact]
    [Covers("import run")]
    [CoversFlags("import run", "--scenario", "--file", "--validate-only", "--break-on-error")]
    public async Task A_csv_import_validates_without_running()
    {
        IReadOnlyList<IReadOnlyList<string>> table = await VendorTableAsync();
        var file = LiveCli.TempFile("velixo-it-vendors.csv");
        await File.WriteAllTextAsync(file, string.Concat(table.Select(row => string.Join(',', row.Select(Csv)) + "\n")), TestContext.Current.CancellationToken);

        CliRun run = (await LiveCli.RunAsync(
            "import", "run", "--connection", LiveCli.Connection, "--scenario", Scenario, "--file", file, "--validate-only", "--break-on-error")).Expect();

        Assert.False((bool?)run.Json!["result"]!["data"]!["executed"], run.ToString());
        Assert.Equal(1, (int?)run.Json!["result"]!["data"]!["file"]!["rows"]);
    }

    [LiveFact]
    [Covers("import run")]
    [CoversFlags("import run", "--file-token", "--sheet", "--range")]
    public async Task A_staged_workbook_imports_from_its_sheet_and_range_without_running()
    {
        // The vendors sit on the second sheet, from B2, below a blank row and beside a blank column; the
        // first sheet is in the way. Only --sheet and --range find them.
        IReadOnlyList<IReadOnlyList<string>> table = await VendorTableAsync();
        var file = LiveCli.TempFile("velixo-it-vendors.xlsx");
        File.Delete(file);
        WriteWorkbook(file, [("Notes", [["Not the data"]]), ("Vendors", table)], firstCell: "B2");
        var range = $"B2:{ColumnName(table[0].Count)}{table.Count + 1}";

        var token = (string?)(await LiveCli.RunAsync("files", "stage", file)).Expect().Json!["fileToken"];
        CliRun run = (await LiveCli.RunAsync(
            "import", "run", "--connection", LiveCli.Connection, "--scenario", Scenario, "--file-token", token!, "--sheet", "Vendors", "--range", range, "--validate-only")).Expect();

        JsonNode data = run.Json!["result"]!["data"]!;
        Assert.False((bool?)data["executed"], run.ToString());
        Assert.Equal("Vendors", (string?)data["file"]!["sheet"]);
        Assert.Equal(1, (int?)data["file"]!["rows"]);
    }

    /// <summary>The scenario's columns and one row for the test vendor, which a validation never imports.</summary>
    private static async Task<IReadOnlyList<IReadOnlyList<string>>> VendorTableAsync()
    {
        CliRun scenarios = (await LiveCli.RunAsync("import", "scenarios", "--connection", LiveCli.Connection, "--filter", Scenario)).Expect();
        JsonNode? vendors = scenarios.Json!.AsArray().FirstOrDefault(entry => (string?)entry?["name"] == Scenario);
        Assert.True(vendors?["columns"] is JsonArray, scenarios.ToString());

        var columns = vendors!["columns"]!.AsArray().Select(column => column is JsonValue ? (string)column! : (string)column!["name"]!).ToList();
        var values = columns.Select(column => column switch
        {
            "Vendor ID" => TestVendor,
            "Description" => "Velixo CLI integration test, never imported",
            _ => string.Empty,
        }).ToList();
        return [columns, values];
    }

    private static async Task PutAsync(string? url, byte[] bytes)
    {
        Assert.False(string.IsNullOrEmpty(url), "The output has no uploadUrl.");
        using var http = new HttpClient();
        using HttpResponseMessage put = await http.PutAsync(url, new ByteArrayContent(bytes), TestContext.Current.CancellationToken);
        Assert.True(put.IsSuccessStatusCode, $"The upload URL answered HTTP {(int)put.StatusCode}.");
    }
}
