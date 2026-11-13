using System.Globalization;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using static Velixo.Cli.IntegrationTests.LiveHelpers;

namespace Velixo.Cli.IntegrationTests;

/// <summary>
/// Writes, attachments and the builders on Sage Intacct and Business Central, as <see cref="ErpWriteTests"/>
/// and the build tests do on Acumatica. Each run writes only one document for the vendor
/// <see cref="LiveHelpers.TestVendor"/>, which it deletes in a <c>finally</c>, then checks that the vendor has
/// none left: a draft AP bill on Sage Intacct, never posted, and an unposted purchase invoice on Business
/// Central. When the vendor is missing, the test creates it once; it stays. The batches are under
/// <c>Fixtures/sage-intacct/</c> and <c>Fixtures/business-central/</c>.
/// <para>
/// <c>ERP_ACTION_REFUSED</c> has no test on these two ERPs: an action they should refuse, such as a post,
/// could instead succeed and post a document, which cannot be undone.
/// </para>
/// </summary>
public sealed class DocumentWriteTests
{
    /// <summary>The ERPs on which a test of this run has already found or created the vendor. A failure is not kept, so one timeout fails one test.</summary>
    private static readonly HashSet<Erp> _vendorsReady = [];

    [LiveTheory]
    [InlineData(Erp.SageIntacct)]
    [InlineData(Erp.BusinessCentral)]
    [Covers("command run")]
    public async Task A_document_is_saved_with_rows_and_files_that_download_and_it_is_deleted(Erp erp)
    {
        await VendorReadyAsync(erp);
        TestDocument document = TestDocument.For(erp);
        var connection = LiveCli.ConnectionOf(erp);
        var folder = LiveCli.FolderOf(erp);
        var byPath = LiveCli.TempFile($"{folder}-by-path.txt");
        var byToken = LiveCli.TempFile($"{folder}-by-token.txt");
        await File.WriteAllTextAsync(byPath, "Velixo CLI integration test, attached by its path.\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(byToken, "Velixo CLI integration test, attached by a staged token.\n", TestContext.Current.CancellationToken);
        var token = (string?)(await LiveCli.RunAsync("files", "stage", byToken)).Expect().Json!["fileToken"];
        var rows = LiveCli.TempFile($"{folder}-rows.json");
        await File.WriteAllTextAsync(rows, new JsonArray(document.Row(1), document.Row(2)).ToJsonString(), TestContext.Current.CancellationToken);

        CliRun created = (await LiveCli.RunAsync(
            [
                "command", "run", "--batch", LiveCli.Fixture(erp, document.Create), .. document.HeaderParameters(), "--rows", rows,
                "--attach", byPath, "--attach-comment", "Attached by its path", "--attach-token", token!, "--attach-comment", "Attached by a staged token",
                "--yes", "--connection", connection,
            ])).Expect();
        JsonNode data = created.Json!["result"]!["data"]!;
        var key = document.KeyOf(data);
        Assert.False(string.IsNullOrEmpty(key), created.ToString());

        try
        {
            Assert.Equal(2, await LineCountAsync(erp, document.LineQuery(key!)));

            // A read: no --yes. Both files come back under their own names, as they were sent.
            var directory = Directory.CreateTempSubdirectory($"velixo-it-{folder}-files-").FullName;
            CliRun files = (await LiveCli.RunAsync(
                "command", "run", "--batch", LiveCli.Fixture(erp, document.Files), "--param", $"{document.KeyParameter}={key}",
                "--download-dir", directory, "--connection", connection)).Expect();
            Assert.Equal(2, files.Json!["result"]!["attachments"]!.AsArray().Count);
            Assert.Equal(await File.ReadAllTextAsync(byPath, TestContext.Current.CancellationToken), await File.ReadAllTextAsync(Path.Combine(directory, Path.GetFileName(byPath)), TestContext.Current.CancellationToken));
            Assert.Equal(await File.ReadAllTextAsync(byToken, TestContext.Current.CancellationToken), await File.ReadAllTextAsync(Path.Combine(directory, Path.GetFileName(byToken)), TestContext.Current.CancellationToken));
        }
        finally
        {
            (await LiveCli.RunAsync(
                "command", "run", "--batch", LiveCli.Fixture(erp, document.Delete), "--param", $"{document.KeyParameter}={key}", "--yes", "--connection", connection)).Expect();

            // Sage Intacct keeps a record's files as an attachment record of their own, which outlives the bill.
            if (erp == Erp.SageIntacct && (string?)data["attachment"]?["id"] is { } attachment)
            {
                (await LiveCli.RunAsync(
                    "command", "run", "--batch", LiveCli.Fixture(erp, "attachment-delete.batch.json"), "--param", $"AttachmentID={attachment}", "--yes", "--connection", connection)).Expect();
            }
        }

        CliRun left = (await LiveCli.RunAsync("query", "run", "--query", LiveCli.Fixture(erp, document.DocumentsQuery), "--all", "--connection", connection)).Expect();
        Assert.Empty(Rows(left));
    }

    [LiveTheory]
    [InlineData(Erp.SageIntacct)]
    [InlineData(Erp.BusinessCentral)]
    [Covers("query build", "command build")]
    public async Task The_builders_build_a_query_and_a_read_that_run(Erp erp)
    {
        await VendorReadyAsync(erp);
        var connection = LiveCli.ConnectionOf(erp);
        var plan = LiveCli.TempFile($"{LiveCli.FolderOf(erp)}-vendors.plan.json");
        var batch = LiveCli.TempFile($"{LiveCli.FolderOf(erp)}-vendor-read.batch.json");
        string[] numbers = [];

        try
        {
            CliRun query = (await LiveCli.RunAsync(
                "query", "build", "List the vendors: the vendor ID first, then the vendor name, sorted by vendor ID", "--save", plan, "--connection", connection)).Expect();
            CliRun read = (await LiveCli.RunAsync(
                "command", "build", "Read the name of vendor {{VendorID}}", "--save", batch, "--connection", connection)).Expect();
            numbers = [LibraryNumberOf(query), LibraryNumberOf(read)];

            Assert.NotEmpty(Rows((await LiveCli.RunAsync("query", "run", "--plan", plan, "--top", "3", "--connection", connection)).Expect()));

            // On these ERPs a read may hold only Get, Clear, Key, ListAttachments and DownloadAttachments, so it runs without --yes.
            CliRun vendor = (await LiveCli.RunAsync("command", "run", "--batch", batch, "--param", $"VendorID={TestVendor}", "--connection", connection)).Expect();
            Assert.Contains("Velixo CLI integration tests", vendor.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            if (numbers.Length == 2)
            {
                await LiveCli.RunAsync("query", "delete", numbers[0]);
                await LiveCli.RunAsync("command", "delete", numbers[1]);
            }
        }
    }

    [LiveFact]
    [Covers("command run")]
    public async Task Business_central_keeps_no_record_open_between_calls()
    {
        // The read batch: the server refuses --leave-open before it runs anything.
        CliRun run = await LiveCli.RunAsync(
            "command", "run", "--batch", LiveCli.Fixture(Erp.BusinessCentral, "customer-read.batch.json"), "--param", "CustomerID=10000",
            "--leave-open", "--yes", "--connection", LiveCli.BusinessCentralConnection);

        run.ExpectError(ExitCodes.ServerRefused, ErrorCodes.BcRecordSessionUnsupported);
    }

    private static async Task<int> LineCountAsync(Erp erp, string query) =>
        Rows((await LiveCli.RunWithInputAsync(query, "query", "run", "--query", "-", "--connection", LiveCli.ConnectionOf(erp))).Expect()).Count;

    private static async Task VendorReadyAsync(Erp erp)
    {
        if (!_vendorsReady.Contains(erp))
        {
            await EnsureVendorAsync(erp);
            _vendorsReady.Add(erp);
        }
    }

    private static async Task EnsureVendorAsync(Erp erp)
    {
        var connection = LiveCli.ConnectionOf(erp);
        CliRun vendor = (await LiveCli.RunAsync("query", "run", "--query", LiveCli.Fixture(erp, "test-vendor.vql"), "--connection", connection)).Expect();
        if (Rows(vendor).Count > 0)
        {
            return;
        }

        var id = erp == Erp.SageIntacct ? "VendorID" : "VendorNo";
        (await LiveCli.RunAsync(
            "command", "run", "--batch", LiveCli.Fixture(erp, "vendor-create.batch.json"), "--param", $"{id}={TestVendor}",
            "--param", "VendorName=Velixo CLI integration tests", "--yes", "--connection", connection)).Expect();
        if (erp == Erp.BusinessCentral)
        {
            // A purchase invoice needs the vendor's posting groups, which a new vendor does not get by itself.
            (await LiveCli.RunAsync(
                "command", "run", "--batch", LiveCli.Fixture(erp, "vendor-posting-groups.batch.json"), "--param", $"VendorNo={TestVendor}",
                "--param", "GenBusPostingGroup=DOMESTIC", "--param", "VendorPostingGroup=DOMESTIC", "--param", "VatBusPostingGroup=DOMESTIC",
                "--yes", "--connection", connection)).Expect();
        }
    }

    /// <summary>The test document of one ERP: its batches, the values they take, and how to find it again.</summary>
    private sealed record TestDocument(
        string Create,
        string Files,
        string Delete,
        string DocumentsQuery,
        string KeyParameter,
        Func<string[]> HeaderParameters,
        Func<int, JsonObject> Row,
        Func<JsonNode, string?> KeyOf,
        Func<string, string> LineQuery)
    {
        public static TestDocument For(Erp erp) => erp switch
        {
            Erp.SageIntacct => new(
                "bill-create.batch.json",
                "bill-files.batch.json",
                "bill-delete.batch.json",
                "test-vendor-bills.vql",
                "BillKey",
                () =>
                [
                    "--param", $"VendorID={TestVendor}", "--param", $"BillDate={Today(0)}", "--param", $"DueDate={Today(30)}",
                    "--param", "Currency=USD", "--param", "BaseCurrency=USD", "--param", $"VendorRef={Reference()}",
                    "--param", "Description=Velixo CLI integration test, deleted by the test",
                ],
                line => new JsonObject
                {
                    ["values"] = new JsonObject
                    {
                        ["AccountNo"] = "60340",
                        ["LocationID"] = "100",
                        ["Amount"] = line.ToString(CultureInfo.InvariantCulture),
                        ["LineMemo"] = $"Velixo CLI integration test, row {line}",
                    },
                },
                data => (string?)data["key"],
                bill => $"FROM [accounts-payable/bill-line] AS Line\nWHERE Line.bill.key = '{bill}'\nSELECT Line.key AS 'Line'\n"),
            Erp.BusinessCentral => new(
                "invoice-create.batch.json",
                "invoice-files.batch.json",
                "invoice-delete.batch.json",
                "test-vendor-invoices.vql",
                "InvoiceNo",
                () => ["--param", $"VendorNo={TestVendor}", "--param", $"VendorInvoiceNo={Reference()}"],
                line => new JsonObject
                {
                    ["values"] = new JsonObject
                    {
                        ["AccountNo"] = "8210",
                        ["Quantity"] = "1",
                        ["UnitCost"] = line.ToString(CultureInfo.InvariantCulture),
                    },
                },
                data => (string?)data["fields"]?["number"],
                invoice => $"FROM [Purchase Line] AS Line\nWHERE Line.[Document Type] = 'Invoice' AND Line.[Document No.] = '{invoice}'\nSELECT Line.[Line No.] AS 'Line'\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(erp), erp, "The Acumatica writes are in ErpWriteTests."),
        };

        private static string Today(int days) => DateTime.UtcNow.Date.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>A vendor reference no other document has.</summary>
        private static string Reference() => "IT-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
    }
}
