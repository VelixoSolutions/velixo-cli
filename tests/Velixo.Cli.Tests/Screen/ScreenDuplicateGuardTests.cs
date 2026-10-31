using System.Net;
using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Screen;

namespace Velixo.Cli.Tests.Screen;

public sealed class ScreenDuplicateGuardTests
{
    [Fact]
    public async Task The_same_write_within_10_minutes_is_refused()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        test.Mcp.ThenText(ScreenHarness.Done());

        Assert.Equal(ExitCodes.Success, await Run(test, batch));
        test.Console.OutWriter.GetStringBuilder().Clear();
        test.Clock.Advance(TimeSpan.FromMinutes(9));

        var exit = await Run(test, batch);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(ErrorCodes.DuplicateRun, (string?)test.Error["code"]);
        Assert.Contains("--force", (string?)test.Error["nextStep"], StringComparison.Ordinal);
        Assert.Single(test.Mcp.Calls);
    }

    [Fact]
    public async Task Force_runs_the_same_write_again()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        test.Mcp.ThenText(ScreenHarness.Done()).ThenText(ScreenHarness.Done());

        await Run(test, batch);
        var exit = await Run(test, batch, "--force");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(2, test.Mcp.Calls.Count);
    }

    [Fact]
    public async Task Other_values_are_another_write()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        test.Mcp.ThenText(ScreenHarness.Done()).ThenText(ScreenHarness.Done());

        await Run(test, batch);
        var exit = await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=OTHER", "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(2, test.Mcp.Calls.Count);
    }

    [Fact]
    public async Task The_write_is_recorded_before_the_call_so_the_same_write_started_meanwhile_is_refused()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        int? recordedDuringTheCall = null;
        test.Mcp.ThenText(() =>
        {
            recordedDuringTheCall = test.State.RecentWrites.Count;
            return ScreenHarness.Done();
        });

        Assert.Equal(ExitCodes.Success, await Run(test, batch));

        Assert.Equal(1, recordedDuringTheCall);
        RecentWrite write = Assert.Single(test.State.RecentWrites);
        Assert.Equal(test.Clock.Now, write.ExecutedAt);
    }

    [Fact]
    public async Task Another_attachment_is_another_write()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        var first = test.Write("first.jpg", "first receipt");
        var second = test.Write("second.jpg", "second receipt");
        var again = test.Write("again.jpg", "first receipt");
        test.Mcp
            .ThenText("""{"fileToken":"ft_1","uploadUrl":"https://nx-dev.velixo.com/mcp/upload/1"}""").ThenText(ScreenHarness.Done())
            .ThenText("""{"fileToken":"ft_2","uploadUrl":"https://nx-dev.velixo.com/mcp/upload/2"}""").ThenText(ScreenHarness.Done());
        test.Http.ThenStatus(HttpStatusCode.OK).ThenStatus(HttpStatusCode.OK);

        Assert.Equal(ExitCodes.Success, await Run(test, batch, "--attach", first));
        test.Console.OutWriter.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await Run(test, batch, "--attach", second));
        test.Console.OutWriter.GetStringBuilder().Clear();

        Assert.Equal(ExitCodes.Usage, await Run(test, batch, "--attach", again));
        Assert.Equal(ErrorCodes.DuplicateRun, (string?)test.Error["code"]);
    }

    [Fact]
    public async Task A_write_older_than_10_minutes_runs_again_and_is_pruned()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        test.Mcp.ThenText(ScreenHarness.Done()).ThenText(ScreenHarness.Done());

        await Run(test, batch);
        test.Clock.Advance(TimeSpan.FromMinutes(11));
        var exit = await Run(test, batch);

        Assert.Equal(ExitCodes.Success, exit);
        RecentWrite write = Assert.Single(test.State.RecentWrites);
        Assert.Equal(test.Clock.Now, write.ExecutedAt);
    }

    [Fact]
    public async Task A_read_only_batch_is_never_refused()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("read.batch.json", ScreenHarness.ReadBatch);
        test.Mcp.ThenText(ScreenHarness.Done()).ThenText(ScreenHarness.Done());

        await test.RunAsync("command", "run", "--batch", batch, "--intent", ScreenHarness.Intent);
        var exit = await test.RunAsync("command", "run", "--batch", batch, "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(test.State.RecentWrites);
    }

    [Fact]
    public async Task A_write_that_failed_in_the_ERP_counts_because_part_of_it_may_have_saved()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        test.Mcp.ThenText("""{"taskId":"t_9","status":"failed","error":"Acumatica returned HTTP 500."}""", isError: true);

        Assert.Equal(ExitCodes.TaskFailed, await Run(test, batch));
        test.Console.OutWriter.GetStringBuilder().Clear();

        Assert.Equal(ExitCodes.Usage, await Run(test, batch));
        Assert.Equal(ErrorCodes.DuplicateRun, (string?)test.Error["code"]);
    }

    [Fact]
    public void The_fingerprint_ignores_the_order_of_the_variables()
    {
        var first = JsonNode.Parse("""{"target":"AR301000","variables":{"A":"1","B":null}}""")!.AsObject();
        var second = JsonNode.Parse("""{"variables":{"B":null,"A":"1"},"target":"AR301000"}""")!.AsObject();
        var third = JsonNode.Parse("""{"variables":{"B":"","A":"1"},"target":"AR301000"}""")!.AsObject();

        Assert.Equal(ScreenDuplicateGuard.Fingerprint(first), ScreenDuplicateGuard.Fingerprint(second));
        Assert.NotEqual(ScreenDuplicateGuard.Fingerprint(first), ScreenDuplicateGuard.Fingerprint(third));
    }

    [Fact]
    public async Task A_batch_that_only_reads_runs_without_yes_and_again_at_once()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("image.batch.json", ImageBatch);
        test.Mcp.ThenText(ScreenHarness.Done()).ThenText(ScreenHarness.Done());

        Assert.Equal(ExitCodes.Success, await ReadImage(test, batch));
        test.Console.OutWriter.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await ReadImage(test, batch));

        Assert.Equal(2, test.Mcp.Calls.Count);
        Assert.Empty(test.Profiles.LoadState("prod").RecentWrites);
    }

    [Fact]
    public async Task A_batch_that_sets_a_note_still_needs_yes()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("note.batch.json", """
            {
              "target": "IN202500",
              "commands": [
                { "type": "Set", "name": "Item.InventoryCD", "value": "{{InventoryID}}" },
                { "type": "Set", "name": "Item.NoteText", "value": "Checked" }
              ]
            }
            """);

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--param", "InventoryID=AACOMPUT01", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(ErrorCodes.ConfirmationRequired, (string?)test.Error["code"]);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_batch_that_only_reads_still_needs_yes_when_it_attaches_a_file()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("image.batch.json", ImageBatch);
        var file = test.Write("photo.jpg", "jpg");

        var exit = await test.RunAsync(
            "command", "run", "--batch", batch, "--param", "InventoryID=AACOMPUT01", "--param", "ImageFile=AACOMPUT01.jpg", "--attach", file, "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(ErrorCodes.ConfirmationRequired, (string?)test.Error["code"]);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_batch_that_only_reads_still_needs_yes_when_it_leaves_its_record_open()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("image.batch.json", ImageBatch);

        var exit = await test.RunAsync(
            "command", "run", "--batch", batch, "--param", "InventoryID=AACOMPUT01", "--param", "ImageFile=AACOMPUT01.jpg", "--leave-open", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(ErrorCodes.ConfirmationRequired, (string?)test.Error["code"]);
        Assert.Empty(test.Mcp.Calls);
    }

    private const string ImageBatch = """
        {
          "target": "IN202500",
          "commands": [
            { "type": "Clear" },
            { "type": "Set", "name": "Item.InventoryCD", "value": "{{InventoryID}}", "commit": null },
            { "type": "DownloadAttachments", "name": "Item", "value": "{{ImageFile}}" }
          ]
        }
        """;

    private static Task<int> ReadImage(ScreenHarness test, string batch) =>
        test.RunAsync("command", "run", "--batch", batch, "--param", "InventoryID=AACOMPUT01", "--param", "ImageFile=AACOMPUT01.jpg", "--intent", ScreenHarness.Intent);

    private static Task<int> Run(ScreenHarness test, string batch, params string[] extra) =>
        test.RunAsync(["command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--yes", "--intent", ScreenHarness.Intent, .. extra]);
}
