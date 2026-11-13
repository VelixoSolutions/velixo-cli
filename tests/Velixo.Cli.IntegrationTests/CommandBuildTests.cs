using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using static Velixo.Cli.IntegrationTests.LiveHelpers;

namespace Velixo.Cli.IntegrationTests;

/// <summary>
/// <c>command build</c> and the profile's list of batches, on the Acumatica connection. These are the only
/// screen tests that call the builder, a model, so they check what the CLI does with its answer.
/// </summary>
public sealed class CommandBuildTests
{
    private const string Name = "velixo-it-customer-read";

    [LiveFact]
    [Covers("command build", "command list", "command run", "command delete")]
    [CoversFlags("command build", "--target", "--name", "--save", "--accept-defaults")]
    public async Task A_build_on_a_named_screen_is_saved_and_runs_and_is_deleted_by_its_name()
    {
        var saved = LiveCli.TempFile("customer-status.batch.json");
        File.Delete(saved);
        JsonArray customer = Rows(
            (await LiveCli.RunAsync("query", "run", "--query", LiveCli.Fixture(Erp.Acumatica, "customers.vql"), "--top", "1", "--connection", LiveCli.Connection)).Expect())[0]!.AsArray();

        CliRun build = (await LiveCli.RunAsync(
            "command", "build", "Read the name and the status of customer {{CustomerID}}", "--target", "AR303000", "--name", Name,
            "--save", saved, "--accept-defaults", "--connection", LiveCli.Connection)).Expect();
        Assert.Equal(Name, (string?)build.Json!["libraryName"]);
        Assert.Equal("AR303000", (string?)build.Json!["result"]!["data"]!["target"]);

        // The saved file is the same batch, whatever the output.
        JsonNode file = JsonNode.Parse(await File.ReadAllTextAsync(saved, TestContext.Current.CancellationToken))!;
        Assert.Equal(CommandTypes(build.Json!), CommandTypes(file));

        // Before running it: the batch only loads and reads, so it is no write on Acumatica.
        var types = CommandTypes(build.Json!);
        Assert.True(types.All(type => type is "Get" or "Clear" or "Set" or "Key"), $"The batch does more than read: {string.Join(", ", types)}\n{build}");

        try
        {
            CliRun listed = (await LiveCli.RunAsync("command", "list")).Expect();
            Assert.Contains(listed.Json!.AsArray(), entry => (string?)entry?["name"] == Name);

            // By its name: the entry carries the connection it was built on.
            CliRun run = (await LiveCli.RunAsync("command", "run", Name, "--param", $"CustomerID={customer[0]}")).Expect();
            Assert.Contains((string)customer[1]!, run.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            (await LiveCli.RunAsync("command", "delete", Name)).Expect();
        }

        CliRun after = (await LiveCli.RunAsync("command", "list")).Expect();
        Assert.DoesNotContain(after.Json!.AsArray(), entry => (string?)entry?["name"] == Name);
    }

    [LiveFact]
    [Covers("command build")]
    public async Task A_request_the_builder_cannot_answer_is_a_refusal_and_saves_nothing()
    {
        var saved = LiveCli.TempFile("refused.batch.json");
        File.Delete(saved);

        CliRun build = await LiveCli.RunAsync("command", "build", QueryBuildTests.RefusedRequest, "--save", saved, "--connection", LiveCli.Connection);

        build.ExpectError(ExitCodes.BuilderRefusal, ErrorCodes.BuilderRefusal);
        Assert.False(File.Exists(saved), build.ToString());
    }
}
