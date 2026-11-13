using System.Globalization;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using static Velixo.Cli.IntegrationTests.LiveHelpers;

namespace Velixo.Cli.IntegrationTests;

/// <summary>
/// The tests that write to the ERP, on <see cref="LiveCli.Connection"/> only (gab-dev), and only AP bills (AP301000) for the
/// test vendor <see cref="LiveHelpers.TestVendor"/>. A bill is created on hold, which gab-dev gives a new
/// bill, and each test deletes the bills it saved in a <c>finally</c>, then checks that the vendor has
/// none left. When the vendor is missing, the first test creates it once, in the class GNA; it stays.
/// The batches are under <c>Fixtures/acumatica/</c>:
/// <list type="bullet">
/// <item><c>ap-bill-create.batch.json</c>: a bill with a description, a vendor reference and one detail row
/// per <c>--rows</c> entry, then Save. Built by <c>command build</c>, with its <c>PutOnHold</c> removed by
/// hand: a new bill is already on hold, and the ERP refuses the action then.</item>
/// <item><c>ap-bill-files.batch.json</c>: downloads a bill's attachments, a read.</item>
/// <item><c>ap-bill-release.batch.json</c>: releases a bill, which the ERP refuses while it is on hold.</item>
/// <item><c>ap-bill-delete.batch.json</c>: deletes a bill.</item>
/// <item><c>vendor-create.batch.json</c>, <c>test-vendor.vql</c>, <c>test-vendor-bills.vql</c>: the vendor and its bills.</item>
/// </list>
/// </summary>
public sealed class ErpWriteTests
{
    /// <summary>A vendor ID the tests never save: creating it without a vendor class fails on gab-dev.</summary>
    private const string UnsavedVendor = "VELIXOITNS";

    private static readonly Lazy<Task> _vendor = new(EnsureVendorAsync);

    [LiveFact]
    [Covers("command run")]
    [CoversFlags("command run", "--rows", "--param-null", "--attach", "--attach-token", "--attach-comment", "--download-dir")]
    [CoversFlags(CoversFlagsAttribute.Shared, "--yes")]
    public async Task A_bill_is_saved_on_hold_with_rows_and_files_its_release_is_refused_and_it_is_deleted()
    {
        await _vendor.Value;
        var byPath = LiveCli.TempFile("bill-by-path.txt");
        var byToken = LiveCli.TempFile("bill-by-token.txt");
        await File.WriteAllTextAsync(byPath, "Velixo CLI integration test, attached by its path.\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(byToken, "Velixo CLI integration test, attached by a staged token.\n", TestContext.Current.CancellationToken);
        var token = (string?)(await LiveCli.RunAsync("files", "stage", byToken)).Expect().Json!["fileToken"];
        Assert.False(string.IsNullOrEmpty(token));

        // The description is null: the batch's command that writes it is dropped, and the field keeps its default.
        CliRun created = (await LiveCli.RunAsync(
            "command", "run", "--batch", Batch("ap-bill-create"), "--param", $"VendorID={TestVendor}", "--param", $"VendorRef={VendorReference()}",
            "--param-null", "Description", "--rows", RowsFile("bill-rows.json", 1, 2),
            "--attach", byPath, "--attach-comment", "Attached by its path", "--attach-token", token!, "--attach-comment", "Attached by a staged token",
            "--yes", "--connection", LiveCli.Connection)).Expect();
        var bill = SavedBill(created);

        try
        {
            JsonNode document = created.Json!["result"]!["feedback"]!["record"]!["Document"]!;
            Assert.Equal("On Hold", (string?)document["Status"]);
            Assert.Contains(created.Json!["result"]!["skippedFields"]!.AsArray(), field => (string?)field?["variable"] == "Description");
            Assert.Equal(2, await LineCountAsync(bill));

            // A read: no --yes. Both files come back, each into the folder under its own name.
            var directory = Directory.CreateTempSubdirectory("velixo-it-bill-files-").FullName;
            CliRun files = (await LiveCli.RunAsync(
                "command", "run", "--batch", Batch("ap-bill-files"), "--param", $"RefNbr={bill}", "--download-dir", directory, "--connection", LiveCli.Connection)).Expect();
            var paths = files.Json!["result"]!["attachments"]!.AsArray().Select(file => (string?)file?["path"]).ToList();
            Assert.Equal(2, paths.Count);
            Assert.Equal(await File.ReadAllTextAsync(byPath, TestContext.Current.CancellationToken), await File.ReadAllTextAsync(Path.Combine(directory, Path.GetFileName(byPath)), TestContext.Current.CancellationToken));
            Assert.Equal(await File.ReadAllTextAsync(byToken, TestContext.Current.CancellationToken), await File.ReadAllTextAsync(Path.Combine(directory, Path.GetFileName(byToken)), TestContext.Current.CancellationToken));

            // A bill on hold cannot be released: the ERP refuses the action, and nothing changes. The batch only
            // opened the bill before the Release, so the server says that nothing was written (RD-15492).
            CliRun release = (await LiveCli.RunAsync("command", "run", "--batch", Batch("ap-bill-release"), "--param", $"RefNbr={bill}", "--yes", "--connection", LiveCli.Connection))
                .ExpectError(ExitCodes.TaskFailed, ErrorCodes.ErpActionRefused);
            Assert.Contains("NOTHING was written", (string?)release.Json!["error"]!["message"], StringComparison.Ordinal);
        }
        finally
        {
            await DeleteAsync(bill);
        }

        Assert.DoesNotContain(bill, await BillsAsync());
    }

    [LiveFact]
    [Covers("command run")]
    [CoversFlags("command run", "--leave-open", "--continue", "--continuation", "--finish")]
    public async Task A_bill_left_open_takes_more_rows_and_is_finished_from_a_saved_continuation()
    {
        await _vendor.Value;
        var batch = Batch("ap-bill-create");
        string? bill = null;

        try
        {
            // The header and the first row, the record left open: nothing is saved yet.
            CliRun opened = (await LiveCli.RunAsync(
                "command", "run", "--batch", batch, "--param", $"VendorID={TestVendor}", "--param", $"VendorRef={VendorReference()}",
                "--param", "Description=Velixo CLI integration test, continued", "--rows", RowsFile("continued-1.json", 1),
                "--leave-open", "--yes", "--connection", LiveCli.Connection)).Expect();
            Assert.Equal("AP301000", (string?)opened.Json!["result"]!["continuation"]!["screenId"]);

            // The stored continuation, and the record still open after it.
            CliRun continued = (await LiveCli.RunAsync(
                "command", "run", "--batch", batch, "--continue", "--rows", RowsFile("continued-2.json", 2), "--leave-open", "--yes", "--connection", LiveCli.Connection)).Expect();
            Assert.NotNull(continued.Json!["result"]!["continuation"]);
            var saved = LiveCli.TempFile("continued.result.json");
            await File.WriteAllTextAsync(saved, continued.StdOut, TestContext.Current.CancellationToken);

            // The saved result, as another process would read it, and the finish set: Save.
            CliRun finished = (await LiveCli.RunAsync(
                "command", "run", "--batch", batch, "--continuation", saved, "--rows", RowsFile("continued-3.json", 3), "--finish", "--yes", "--connection", LiveCli.Connection)).Expect();
            bill = SavedBill(finished);
            Assert.Null(finished.Json!["result"]!["continuation"]);
            Assert.Equal(3, await LineCountAsync(bill));
        }
        finally
        {
            if (bill is not null)
            {
                await DeleteAsync(bill);
            }
        }

        Assert.DoesNotContain(bill, await BillsAsync());
    }

    [LiveFact]
    [Covers("command run")]
    public async Task A_bill_whose_save_fails_is_reported_unsaved_with_the_file_it_carried()
    {
        await _vendor.Value;
        var file = LiveCli.TempFile("bill-never-saved.txt");
        await File.WriteAllTextAsync(file, "Velixo CLI integration test, never attached.\n", TestContext.Current.CancellationToken);

        // gab-dev requires a vendor reference, so the Save fails, and the bill and its file never reach the ERP.
        CliRun run = await LiveCli.RunAsync(
            "command", "run", "--batch", Batch("ap-bill-create"), "--param", $"VendorID={TestVendor}", "--param-null", "VendorRef",
            "--param", "Description=Velixo CLI integration test, never saved", "--rows", RowsFile("never-saved.json", 1),
            "--attach", file, "--yes", "--connection", LiveCli.Connection);

        run.ExpectError(ExitCodes.TaskFailed, ErrorCodes.TaskFailed);
        var message = (string?)run.Json!["error"]!["message"];
        Assert.Contains("NOTHING was saved", message, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(file), message, StringComparison.Ordinal);
        Assert.DoesNotContain("the document exists", message, StringComparison.Ordinal);
        Assert.Empty(await BillsAsync());
    }

    [LiveFact]
    [Covers("command run")]
    public async Task A_vendor_whose_save_fails_is_reported_unsaved()
    {
        // gab-dev requires a vendor class, and the batch sets it blank, so the Save fails and no vendor is created. The
        // vendor's key is the ID typed here, so only the screen's record state shows that it was never saved.
        CliRun run = await LiveCli.RunAsync(
            "command", "run", "--batch", Batch("vendor-create"), "--param", $"VendorID={UnsavedVendor}", "--param", "VendorName=Velixo CLI integration test, never saved",
            "--param", "VendorClassID=", "--yes", "--connection", LiveCli.Connection);

        run.ExpectError(ExitCodes.TaskFailed, ErrorCodes.TaskFailed);
        var message = (string?)run.Json!["error"]!["message"];
        Assert.Contains("NOTHING was saved", message, StringComparison.Ordinal);
        Assert.DoesNotContain("PARTIALLY RAN", message, StringComparison.Ordinal);

        var vendors = $"FROM [PX.Objects.AP.Vendor] AS Vendor\nWHERE Vendor.AcctCD = '{UnsavedVendor}'\nSELECT Vendor.AcctCD AS 'Vendor ID'\n";
        Assert.Empty(Rows((await LiveCli.RunWithInputAsync(vendors, "query", "run", "--query", "-", "--connection", LiveCli.Connection)).Expect()));
    }

    [LiveFact]
    [Covers("command run")]
    public async Task A_write_batch_passes_the_dry_run_and_is_refused_without_yes()
    {
        string[] args =
        [
            "command", "run", "--batch", Batch("ap-bill-create"), "--param", $"VendorID={TestVendor}", "--param", "VendorRef=IT-NEVER-SAVED",
            "--param", "Description=Velixo CLI integration test, never saved", "--rows", RowsFile("dry-run.json", 1, 2), "--connection", LiveCli.Connection,
        ];

        CliRun dryRun = (await LiveCli.RunAsync([.. args, "--dry-run"])).Expect();
        Assert.True((bool?)dryRun.Json!["wouldWrite"], dryRun.ToString());
        Assert.Equal(2, (int?)dryRun.Json!["rows"]);

        (await LiveCli.RunAsync(args)).ExpectError(ExitCodes.Usage, ErrorCodes.ConfirmationRequired);
    }

    private static string Batch(string name) => LiveCli.Fixture(Erp.Acumatica, $"{name}.batch.json");

    /// <summary>A vendor reference no other bill has: gab-dev requires one, and may refuse one the vendor already used.</summary>
    private static string VendorReference() => "IT-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>A <c>--rows</c> file with one detail row per number, its unit cost that number.</summary>
    private static string RowsFile(string name, params int[] lines)
    {
        var rows = new JsonArray(lines.Select(line => (JsonNode?)new JsonObject
        {
            ["values"] = new JsonObject
            {
                ["LineDescription"] = $"Velixo CLI integration test, row {line}",
                ["Quantity"] = "1",
                ["UnitCost"] = line.ToString(CultureInfo.InvariantCulture),
            },
        }).ToArray());
        var path = LiveCli.TempFile(name);
        File.WriteAllText(path, rows.ToJsonString());
        return path;
    }

    /// <summary>The reference number the ERP gave the bill a run saved.</summary>
    private static string SavedBill(CliRun run)
    {
        var bill = (string?)run.Json?["result"]?["feedback"]?["savedKey"]?["RefNbr"];
        Assert.False(string.IsNullOrEmpty(bill) || bill.StartsWith('<'), $"No bill was saved.\n{run}");
        return bill!;
    }

    private static async Task DeleteAsync(string bill) =>
        (await LiveCli.RunAsync("command", "run", "--batch", Batch("ap-bill-delete"), "--param", $"RefNbr={bill}", "--yes", "--connection", LiveCli.Connection)).Expect();

    /// <summary>The reference numbers of the test vendor's bills.</summary>
    private static async Task<List<string?>> BillsAsync()
    {
        CliRun run = (await LiveCli.RunAsync(
            "query", "run", "--query", LiveCli.Fixture(Erp.Acumatica, "test-vendor-bills.vql"), "--all", "--connection", LiveCli.Connection)).Expect();
        return Rows(run).Select(row => ((string?)row?[1])?.Trim()).ToList();
    }

    private static async Task<int> LineCountAsync(string bill)
    {
        var query = $"FROM [PX.Objects.AP.APTran] AS Line\nWHERE Line.TranType = 'INV' AND Line.RefNbr = '{bill}'\nSELECT Line.LineNbr AS 'Line'\n";
        return Rows((await LiveCli.RunWithInputAsync(query, "query", "run", "--query", "-", "--connection", LiveCli.Connection)).Expect()).Count;
    }

    private static async Task EnsureVendorAsync()
    {
        CliRun vendor = (await LiveCli.RunAsync("query", "run", "--query", LiveCli.Fixture(Erp.Acumatica, "test-vendor.vql"), "--connection", LiveCli.Connection)).Expect();
        if (Rows(vendor).Count == 0)
        {
            (await LiveCli.RunAsync(
                "command", "run", "--batch", Batch("vendor-create"), "--param", $"VendorID={TestVendor}", "--param", "VendorName=Velixo CLI integration tests",
                "--param", "VendorClassID=GNA", "--yes", "--connection", LiveCli.Connection)).Expect();
        }
    }
}
