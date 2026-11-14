using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using static Velixo.Cli.IntegrationTests.LiveHelpers;

namespace Velixo.Cli.IntegrationTests;

/// <summary>
/// <c>command run</c> reads on each ERP, from the checked-in <c>Fixtures/&lt;erp&gt;/customer-read.batch.json</c>,
/// which reads one customer's name by its ID: a read on every ERP, so it runs without <c>--yes</c>. The
/// customer is the first one <c>customers.vql</c> lists. The builder's own tests are in
/// <see cref="CommandBuildTests"/>, and the tests that write to the ERP in <see cref="ErpWriteTests"/>.
/// </summary>
public sealed class ScreenTests
{
    [LiveTheory]
    [InlineData(Erp.Acumatica)]
    [InlineData(Erp.SageIntacct)]
    [InlineData(Erp.BusinessCentral)]
    [Covers("command run")]
    [CoversFlags("command run", "--batch", "--param", "--params", "--dry-run")]
    public async Task A_read_batch_reads_a_customer_without_confirmation(Erp erp)
    {
        var connection = LiveCli.ConnectionOf(erp);
        var batch = LiveCli.Fixture(erp, "customer-read.batch.json");
        JsonArray customer = Rows(
            (await LiveCli.RunAsync("query", "run", "--query", LiveCli.Fixture(erp, "customers.vql"), "--top", "1", "--connection", connection)).Expect())[0]!.AsArray();
        var (id, name) = ((string)customer[0]!, (string)customer[1]!);

        // The batch reads, as the CLI defines a read for the ERP, so nothing asks for --yes.
        CliRun dryRun = (await LiveCli.RunAsync("command", "run", "--batch", batch, "--param", $"CustomerID={id}", "--dry-run", "--connection", connection)).Expect();
        Assert.True((bool?)dryRun.Json!["dryRun"], dryRun.ToString());
        Assert.False((bool?)dryRun.Json!["wouldWrite"], dryRun.ToString());

        CliRun byParam = (await LiveCli.RunAsync("command", "run", "--batch", batch, "--param", $"CustomerID={id}", "--connection", connection)).Expect();
        Assert.Contains(name, byParam.StdOut, StringComparison.Ordinal);

        var paramsFile = LiveCli.TempFile($"customer-read.{LiveCli.FolderOf(erp)}.params.json");
        await File.WriteAllTextAsync(paramsFile, new JsonObject { ["CustomerID"] = id }.ToJsonString());
        CliRun byFile = (await LiveCli.RunAsync("command", "run", "--batch", batch, "--params", paramsFile, "--connection", connection)).Expect();
        Assert.Contains(name, byFile.StdOut, StringComparison.Ordinal);
    }

    [LiveTheory]
    [InlineData(Erp.Acumatica)]
    [InlineData(Erp.SageIntacct)]
    [InlineData(Erp.BusinessCentral)]
    [Covers("command run")]
    public async Task A_batch_with_a_value_missing_or_unknown_is_refused_before_any_call(Erp erp)
    {
        var batch = LiveCli.Fixture(erp, "customer-read.batch.json");
        var connection = LiveCli.ConnectionOf(erp);

        (await LiveCli.RunAsync("command", "run", "--batch", batch, "--connection", connection))
            .ExpectError(ExitCodes.ServerRefused, ErrorCodes.ScreenVariableMissing);
        (await LiveCli.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=X", "--param", "VelixoItUnknown=1", "--connection", connection))
            .ExpectError(ExitCodes.ServerRefused, ErrorCodes.ScreenVariableUnknown);
    }
}
