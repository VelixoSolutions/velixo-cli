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

    private static Task<int> Run(ScreenHarness test, string batch, params string[] extra) =>
        test.RunAsync(["command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--yes", "--intent", ScreenHarness.Intent, .. extra]);
}
