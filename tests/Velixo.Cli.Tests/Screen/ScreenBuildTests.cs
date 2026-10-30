using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Tests.Screen;

public sealed class ScreenBuildTests
{
    [Fact]
    public async Task Build_prints_the_batch_under_result_data()
    {
        using var test = new ScreenHarness();
        var batch = JsonNode.Parse(ScreenHarness.OrderBatch)!["result"]!["data"]!.ToJsonString();
        test.Mcp.ThenText(ScreenHarness.Done(batch));

        var exit = await test.RunAsync(
            "command", "build", "Create a sales order for customer {{CustomerID}} with the lines I will supply",
            "--target", "SO301000", "--connection", "gab-dev", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        (var name, JsonObject sent) = Assert.Single(test.Mcp.Calls);
        Assert.Equal("prepare_commands", name);
        Assert.Equal(ScreenHarness.Intent, (string?)sent["intent"]);
        Assert.StartsWith("Create a sales order", (string?)sent["request"], StringComparison.Ordinal);
        Assert.Equal("SO301000", (string?)sent["target"]);
        Assert.Equal("gab-dev", (string?)sent["connectionName"]);

        JsonNode data = test.Output["result"]!["data"]!;
        Assert.Equal("SO301000", (string?)data["target"]);
        Assert.Equal(3, data["commands"]!.AsArray().Count);
        Assert.Equal(2, data["lineCommands"]!.AsArray().Count);
        Assert.Equal(2, data["finishCommands"]!.AsArray().Count);
        Assert.Equal(2, data["variables"]!.AsArray().Count);
        Assert.Equal("OrderNbr", (string?)data["keyNumbering"]!["field"]);
    }

    [Fact]
    public async Task Accept_defaults_adds_the_users_leave_to_default_every_unnamed_field_to_the_request()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText(ScreenHarness.Done(JsonNode.Parse(ScreenHarness.OrderBatch)!["result"]!["data"]!.ToJsonString()));

        var exit = await test.RunAsync(
            "command", "build", "Create a sales order for customer {{CustomerID}}", "--accept-defaults",
            "--connection", "gab-dev", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        (_, JsonObject sent) = Assert.Single(test.Mcp.Calls);
        Assert.Equal(
            "Create a sales order for customer {{CustomerID}}. Leave every field this request does not name at its default.",
            (string?)sent["request"]);
    }

    [Theory]
    [InlineData("Release it.", "Release it. Leave every field this request does not name at its default.")]
    [InlineData("Release it  ", "Release it. Leave every field this request does not name at its default.")]
    public void Accept_defaults_ends_the_request_as_one_more_sentence(string request, string expected) =>
        Assert.Equal(expected, Velixo.Cli.Commands.ScreenCommands.WithDefaultsAccepted(request));

    [Fact]
    public async Task Build_on_a_terminal_summarises_the_batch_and_shows_how_to_check_and_run_it()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText(ScreenHarness.Done(JsonNode.Parse(ScreenHarness.OrderBatch)!["result"]!["data"]!.ToJsonString()));

        var exit = await test.RunAsync(
            "command", "build", "Create a sales order for customer {{CustomerID}}", "--save", "order.batch.json",
            "--connection", "gab-dev", "--output", "table", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        var printed = test.Console.StdOut;
        Assert.StartsWith("Screen SO301000\n  7 commands; it changes the record, so a run needs --yes\n", printed, StringComparison.Ordinal);
        Assert.Contains("  CustomerID  string, The customer\n", printed, StringComparison.Ordinal);
        Assert.Contains("values for InventoryID, Qty", printed, StringComparison.Ordinal);
        Assert.Contains("The ERP assigns OrderNbr", printed, StringComparison.Ordinal);
        const string run = "velixo command run --batch order.batch.json --param CustomerID=... --param Note=... --rows rows.json --connection gab-dev";
        Assert.Contains($"  {run} --dry-run", printed, StringComparison.Ordinal);
        Assert.Contains($"  {run} --yes", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("Add --save", printed, StringComparison.Ordinal);
        Assert.Contains("\nvelixo command run --batch - --param CustomerID=... --param Note=... --rows rows.json --connection gab-dev --dry-run <<'JSON'\n{\"target\":\"SO301000\"", printed, StringComparison.Ordinal);
        Assert.EndsWith("\nJSON\n", printed, StringComparison.Ordinal);
        Assert.Equal("SO301000", (string?)JsonNode.Parse(File.ReadAllText(Path.Combine(test.Directory, "order.batch.json")))!["result"]!["data"]!["target"]);
    }

    [Fact]
    public async Task A_read_batch_on_a_terminal_runs_by_its_number_without_yes()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText(ScreenHarness.Done("""{"target":"AR303000","commands":[{"type":"Get","name":"Customer.Name"}]}"""));

        await test.RunAsync("command", "build", "Read a customer's name", "--output", "table", "--intent", ScreenHarness.Intent);

        var printed = test.Console.StdOut;
        Assert.Contains("1 command; it only reads", printed, StringComparison.Ordinal);
        Assert.Contains("Run it\n  velixo command run 1\n", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("--yes", printed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_batch_built_on_windows_shows_the_powershell_form_with_the_dry_run()
    {
        using var test = new ScreenHarness();
        test.Environment.IsWindows = true;
        test.Mcp.ThenText(ScreenHarness.Done("""{"target":"AR303000","commands":[{"type":"Set","name":"Customer.Name","value":"{{Name}}"},{"type":"Action","name":"Save"}]}"""));

        await test.RunAsync("command", "build", "Rename a customer to {{Name}}", "--output", "table", "--intent", ScreenHarness.Intent);

        var printed = test.Console.StdOut;
        Assert.Contains("Or inline (check with --dry-run, then run again with --yes):\n@'\n{\"target\":\"AR303000\"", printed, StringComparison.Ordinal);
        Assert.EndsWith("\n'@ | velixo command run --batch - --param Name=... --dry-run\n", printed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_build_that_asks_a_question_in_its_result_exits_3()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText(ScreenHarness.Done("ERROR: Which screen do you mean: Sales Orders (SO301000) or Quotes (CR304500)?"));

        var exit = await test.RunAsync("command", "build", "Create an order for {{CustomerID}}", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.BuilderRefusal, exit);
        Assert.Equal(ErrorCodes.BuilderRefusal, (string?)test.Error["code"]);
    }

    [Fact]
    public async Task A_build_task_that_failed_with_a_question_exits_3()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText(
            """{"taskId":"t_3","status":"failed","error":"ERROR: Which screen do you mean: Sales Orders (SO301000) or Quotes (CR304500)?"}""",
            isError: true);

        var exit = await test.RunAsync("command", "build", "Create an order for {{CustomerID}}", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.BuilderRefusal, exit);
        Assert.Equal(ErrorCodes.BuilderRefusal, (string?)test.Error["code"]);
        Assert.Equal("t_3", (string?)test.Error["taskId"]);
        Assert.StartsWith("ERROR: Which screen", (string?)test.Error["message"], StringComparison.Ordinal);
    }
}
