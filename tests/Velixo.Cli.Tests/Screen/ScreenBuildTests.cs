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
            "screen", "build", "Create a sales order for customer {{CustomerID}} with the lines I will supply",
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
    public async Task A_build_that_asks_a_question_in_its_result_exits_3()
    {
        using var test = new ScreenHarness();
        test.Mcp.ThenText(ScreenHarness.Done("ERROR: Which screen do you mean: Sales Orders (SO301000) or Quotes (CR304500)?"));

        var exit = await test.RunAsync("screen", "build", "Create an order for {{CustomerID}}", "--intent", ScreenHarness.Intent);

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

        var exit = await test.RunAsync("screen", "build", "Create an order for {{CustomerID}}", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.BuilderRefusal, exit);
        Assert.Equal(ErrorCodes.BuilderRefusal, (string?)test.Error["code"]);
        Assert.Equal("t_3", (string?)test.Error["taskId"]);
        Assert.StartsWith("ERROR: Which screen", (string?)test.Error["message"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_without_intent_is_exit_2_before_any_call()
    {
        using var test = new ScreenHarness();

        var exit = await test.RunAsync("screen", "build", "Create an order for {{CustomerID}}");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(ErrorCodes.IntentRequired, (string?)test.Error["code"]);
        Assert.Empty(test.Mcp.Calls);
    }
}
