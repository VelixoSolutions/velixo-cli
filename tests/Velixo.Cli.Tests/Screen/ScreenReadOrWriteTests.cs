using Velixo.Cli.Output;

namespace Velixo.Cli.Tests.Screen;

/// <summary>Which batches run as a read, without <c>--yes</c>, and which are writes that need it.</summary>
public sealed class ScreenReadOrWriteTests
{
    [Fact]
    public async Task The_same_write_runs_again_when_it_is_run_again()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        test.Mcp.ThenText(ScreenHarness.Done()).ThenText(ScreenHarness.Done());

        Assert.Equal(ExitCodes.Success, await Run(test, batch));
        test.Console.OutWriter.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await Run(test, batch));

        Assert.Equal(2, test.Mcp.Calls.Count);
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

    private static Task<int> Run(ScreenHarness test, string batch) =>
        test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--yes", "--intent", ScreenHarness.Intent);
}
