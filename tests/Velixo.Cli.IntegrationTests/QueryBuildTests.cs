using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using static Velixo.Cli.IntegrationTests.LiveHelpers;

namespace Velixo.Cli.IntegrationTests;

/// <summary>
/// <c>query build</c>, the profile's list of builds, and <c>task status</c>, on the Acumatica connection.
/// These are the only query tests that call the builder, a model: slow, paid and not deterministic, so
/// they check what the CLI does with its answer, never the query it wrote.
/// </summary>
public sealed class QueryBuildTests
{
    private const string Name = "velixo-it-customers";

    [LiveFact]
    [Covers("query build", "query list", "query run", "query delete")]
    [CoversFlags("query build", "--base-query", "--name", "--save")]
    [CoversFlags(CoversFlagsAttribute.Shared, "--quiet")]
    public async Task A_build_refines_a_base_query_saves_it_and_is_run_and_deleted_by_its_name()
    {
        var saved = LiveCli.TempFile("customers-with-status.vql");
        File.Delete(saved);

        CliRun build = (await LiveCli.RunAsync(
            "query", "build", "Add the customer status as a third column", "--base-query", LiveCli.Fixture(Erp.Acumatica, "customers.vql"),
            "--name", Name, "--save", saved, "--quiet", "--connection", LiveCli.Connection)).Expect();

        // --quiet: no progress while the build runs, which takes long enough to print some.
        Assert.True(build.StdErr.Length == 0, build.ToString());
        Assert.Equal(Name, (string?)build.Json!["libraryName"]);

        // A .vql file gets the query text alone, ready for query run --query.
        var text = await File.ReadAllTextAsync(saved, TestContext.Current.CancellationToken);
        Assert.Contains("SELECT", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"queries\"", text, StringComparison.Ordinal);

        try
        {
            CliRun listed = (await LiveCli.RunAsync("query", "list")).Expect();
            Assert.Contains(listed.Json!.AsArray(), entry => (string?)entry?["name"] == Name && (string?)entry?["connection"] == LiveCli.Connection);

            CliRun run = (await LiveCli.RunAsync("query", "run", Name, "--top", "3")).Expect();
            Assert.InRange(Rows(run).Count, 1, 3);
            Assert.Equal(3, QueryData(run)["columns"]!.AsArray().Count);
        }
        finally
        {
            (await LiveCli.RunAsync("query", "delete", Name)).Expect();
        }

        (await LiveCli.RunAsync("query", "run", Name)).Expect(ExitCodes.Usage);
    }

    [LiveFact]
    [Covers("query build", "task status")]
    [CoversFlags("task status", "--wait")]
    [CoversFlags(CoversFlagsAttribute.Shared, "--no-wait")]
    public async Task A_build_that_does_not_wait_is_followed_as_a_task()
    {
        CliRun started = (await LiveCli.RunAsync(
            "query", "build", "List the vendors: the vendor ID first, then the vendor name, sorted by vendor ID",
            "--connection", LiveCli.Connection, "--no-wait")).Expect();
        var taskId = (string?)started.Json!["taskId"];
        Assert.False(string.IsNullOrEmpty(taskId), started.ToString());

        try
        {
            CliRun followed = (await LiveCli.RunAsync("task", "status", taskId!, "--wait")).Expect();
            Assert.Equal("done", (string?)followed.Json!["status"]);
            Assert.NotEmpty(followed.Json!["result"]!["data"]!["queries"]!.AsArray());
        }
        finally
        {
            // A build that finished inside its first call is kept in the profile's list; leave the list as it was.
            if (started.Json!["libraryNumber"] is JsonValue kept)
            {
                await LiveCli.RunAsync("query", "delete", kept.ToString());
            }
        }
    }

    [LiveFact]
    [Covers("query build")]
    public async Task A_request_the_builder_cannot_answer_is_a_refusal_and_saves_nothing()
    {
        var saved = LiveCli.TempFile("refused.plan.json");
        File.Delete(saved);

        CliRun build = await LiveCli.RunAsync("query", "build", RefusedRequest, "--save", saved, "--connection", LiveCli.Connection);

        build.ExpectError(ExitCodes.BuilderRefusal, ErrorCodes.BuilderRefusal);
        Assert.False(string.IsNullOrWhiteSpace((string?)build.Json!["error"]!["message"]), build.ToString());
        Assert.False(File.Exists(saved), build.ToString());
    }

    /// <summary>A request no query of the ERP answers, so the builder asks instead of building.</summary>
    internal const string RefusedRequest = "What will the weather be in Lisbon next Tuesday?";
}
