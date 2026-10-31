using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Mcp;

namespace Velixo.Cli.Tests.Screen;

public sealed class ScreenContinuationTests
{
    private const string Rows = """[{"values":{"InventoryID":"AALEGO500","Qty":"1"}}]""";

    [Fact]
    public async Task Leave_open_stores_the_continuation_with_its_session_and_deadline()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var rows = test.Write("rows.json", Rows);
        test.Mcp.ThenText(ScreenHarness.Done(beside: ScreenHarness.Continuation(17)));

        var exit = await test.RunAsync(
            "command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--param", "Note=Q4", "--rows", rows, "--leave-open", "--yes",
            "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        JsonObject sent = test.Executed;
        Assert.True((bool)sent["leaveOpen"]!);
        Assert.True(sent.ContainsKey("commands"));
        Assert.False(sent.ContainsKey("finishCommands"));

        Assert.Equal(17, (long)test.Output["result"]!["continuation"]!["stamp"]!);
        Assert.Equal("2026-10-28T14:02:00Z", (string?)test.Output["result"]!["continuationExpiresAt"]);

        StoredContinuation stored = test.State.LastContinuation!;
        Assert.Equal("session-1", stored.SessionId);
        Assert.Equal(test.Clock.Now.AddMinutes(2), stored.ExpiresAt);
        Assert.Equal(17, (long)stored.Continuation!["stamp"]!);
    }

    [Fact]
    public async Task Continue_sends_no_header_and_finish_only_with_finish()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var rows = test.Write("rows.json", Rows);
        test.Mcp
            .ThenText(ScreenHarness.Done(beside: ScreenHarness.Continuation(17)))
            .ThenText(ScreenHarness.Done(beside: ScreenHarness.Continuation(18)))
            .ThenText(ScreenHarness.Done());

        await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--param", "Note=Q4", "--rows", rows, "--leave-open", "--yes", "--intent", ScreenHarness.Intent);
        await test.RunAsync("command", "run", "--batch", batch, "--continue", "--rows", rows, "--leave-open", "--yes", "--intent", ScreenHarness.Intent);

        JsonObject second = test.Executed;
        Assert.False(second.ContainsKey("commands"));
        Assert.False(second.ContainsKey("finishCommands"));
        Assert.False(second.ContainsKey("variables"));
        Assert.True(second.ContainsKey("lineCommands"));
        Assert.True(second.ContainsKey("rows"));
        Assert.Equal(17, (long)second["continuation"]!["stamp"]!);

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--continue", "--rows", rows, "--finish", "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        JsonObject third = test.Executed;
        Assert.False(third.ContainsKey("commands"));
        Assert.Equal(2, third["finishCommands"]!.AsArray().Count);
        Assert.Equal(18, (long)third["continuation"]!["stamp"]!);
        Assert.Null(test.State.LastContinuation);
    }

    [Fact]
    public async Task A_var_on_a_continuation_without_finish_binds_nothing()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var rows = test.Write("rows.json", Rows);
        Store(test, stamp: 17, session: "session-1");

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--continue", "--rows", rows, "--param", "Note=x", "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal(ErrorCodes.ScreenVariableUnknown, (string?)test.Error["code"]);
        Assert.Contains("--finish", (string?)test.Error["message"], StringComparison.Ordinal);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_continuation_from_another_MCP_session_is_refused_locally()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var rows = test.Write("rows.json", Rows);
        Store(test, stamp: 17, session: "session-0");

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--continue", "--rows", rows, "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal(ErrorCodes.ScreenSessionLost, (string?)test.Error["code"]);
        Assert.Contains("Nothing was sent", (string?)test.Error["message"], StringComparison.Ordinal);
        Assert.Equal(ErrorGuidance.SessionLostNothingRanStep, (string?)test.Error["nextStep"]);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task An_expired_continuation_is_refused_locally()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var rows = test.Write("rows.json", Rows);
        Store(test, stamp: 17, session: "session-1");
        test.Clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(11));

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--continue", "--rows", rows, "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal(ErrorCodes.ScreenSessionLost, (string?)test.Error["code"]);
        Assert.Contains("expired at 2026-10-28T14:02:00Z", (string?)test.Error["message"], StringComparison.Ordinal);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_continuation_within_the_margin_is_still_sent()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var rows = test.Write("rows.json", Rows);
        Store(test, stamp: 17, session: "session-1");
        test.Clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(5));
        test.Mcp.ThenText(ScreenHarness.Done());

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--continue", "--rows", rows, "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_continuation_for_another_screen_is_refused_locally()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch.Replace("SO301000", "SO302000", StringComparison.Ordinal));
        var rows = test.Write("rows.json", Rows);
        Store(test, stamp: 17, session: "session-1");

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--continue", "--rows", rows, "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal(ErrorCodes.ScreenContinuationMismatch, (string?)test.Error["code"]);
        Assert.Equal(ErrorGuidance.ContinuationMismatchStep, (string?)test.Error["nextStep"]);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task Continue_without_a_stored_continuation_is_a_usage_error()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var rows = test.Write("rows.json", Rows);

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--continue", "--rows", rows, "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(ErrorCodes.NoContinuation, (string?)test.Error["code"]);
    }

    [Fact]
    public async Task Continuation_reads_a_saved_result_and_its_deadline()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var rows = test.Write("rows.json", Rows);
        var saved = test.Write("open.json", $$"""
            { "taskId": "t_1", "status": "done", "result": { {{ScreenHarness.Continuation(41)}}, "continuationExpiresAt": "2026-10-28T13:59:00Z" } }
            """);

        var expired = await test.RunAsync("command", "run", "--batch", batch, "--continuation", saved, "--rows", rows, "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.ServerRefused, expired);
        Assert.Equal(ErrorCodes.ScreenSessionLost, (string?)test.Error["code"]);

        test.Console.OutWriter.GetStringBuilder().Clear();
        File.WriteAllText(saved, $$"""{ {{ScreenHarness.Continuation(42)}} }""");
        test.Mcp.ThenText(ScreenHarness.Done());

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--continuation", saved, "--rows", rows, "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(42, (long)test.Executed["continuation"]!["stamp"]!);
    }

    [Fact]
    public async Task The_chained_example_of_the_spec_runs_end_to_end()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var chunks = Enumerable.Range(1, 4)
            .Select(chunk => test.Write($"lines-{chunk}.json", $$$"""[{"values":{"InventoryID":"ITEM{{{chunk}}}","Qty":"{{{chunk}}}"}}]"""))
            .ToArray();
        test.Mcp
            .ThenText(ScreenHarness.Done(beside: ScreenHarness.Continuation(17)))
            .ThenText(ScreenHarness.Done(beside: ScreenHarness.Continuation(18)))
            .ThenText(ScreenHarness.Done(beside: ScreenHarness.Continuation(19)))
            .ThenText(ScreenHarness.Done(beside: "\"feedback\":{\"record\":{\"OrderType\":\"SO\",\"OrderNbr\":\"SO007231\"}}"));

        Assert.Equal(ExitCodes.Success, await test.RunAsync(
            "command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--param", "Note=Q4", "--rows", chunks[0], "--leave-open", "--yes", "--intent", ScreenHarness.Intent));
        for (var chunk = 1; chunk < 3; chunk++)
        {
            test.Clock.Advance(TimeSpan.FromSeconds(40));
            Assert.Equal(ExitCodes.Success, await test.RunAsync(
                "command", "run", "--batch", batch, "--continue", "--rows", chunks[chunk], "--leave-open", "--yes", "--intent", ScreenHarness.Intent));
        }

        test.Clock.Advance(TimeSpan.FromSeconds(40));
        test.Console.OutWriter.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await test.RunAsync(
            "command", "run", "--batch", batch, "--continue", "--rows", chunks[3], "--finish", "--yes", "--intent", ScreenHarness.Intent));

        var calls = test.Mcp.Calls.Select(call => call.Arguments).ToList();
        Assert.Equal(4, calls.Count);
        Assert.True(calls[0].ContainsKey("commands"));
        Assert.All(calls.Skip(1), call => Assert.False(call.ContainsKey("commands")));
        Assert.Equal([null, 17L, 18L, 19L], calls.Select(call => (long?)call["continuation"]?["stamp"]));
        Assert.Equal([true, true, true, false], calls.Select(call => call.ContainsKey("leaveOpen")));
        Assert.Equal([false, false, false, true], calls.Select(call => call.ContainsKey("finishCommands")));
        Assert.Equal(["ITEM1", "ITEM2", "ITEM3", "ITEM4"], calls.Select(call => (string?)call["rows"]![0]!["values"]!["InventoryID"]));

        Assert.Equal("SO007231", (string?)test.Output["result"]!["feedback"]!["record"]!["OrderNbr"]);
        Assert.Null(test.Output["result"]!["continuation"]);
        Assert.Null(test.State.LastContinuation);
    }

    [Fact]
    public async Task A_session_lost_part_way_is_exit_5_and_ends_the_continuation()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var rows = test.Write("rows.json", Rows);
        Store(test, stamp: 17, session: "session-1");
        test.Mcp.ThenText(
            """{"taskId":"t_7","status":"failed","error":"SCREEN_SESSION_LOST: the screen session that held the open record is gone. Whether any of this call ran is unknown, so read the record before doing anything else. (task id: t_7)"}""",
            isError: true);

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--continue", "--rows", rows, "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal(ErrorCodes.ScreenSessionLost, (string?)test.Error["code"]);
        Assert.Equal(ErrorGuidance.SessionLostUnknownStep, (string?)test.Error["nextStep"]);
        Assert.Equal("t_7", (string?)test.Error["taskId"]);
        Assert.Null(test.State.LastContinuation);
    }

    [Fact]
    public async Task A_mismatch_the_server_found_is_exit_5_with_the_spec_next_step()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var rows = test.Write("rows.json", Rows);
        Store(test, stamp: 17, session: "session-1");
        test.Mcp.ThenText(
            "SCREEN_CONTINUATION_MISMATCH: another call has run on this screen session since the continuation was returned. Nothing was executed.",
            isError: true);

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--continue", "--rows", rows, "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal(ErrorCodes.ScreenContinuationMismatch, (string?)test.Error["code"]);
        Assert.Equal(ErrorGuidance.ContinuationMismatchStep, (string?)test.Error["nextStep"]);
        Assert.Null(test.State.LastContinuation);
    }

    private static void Store(ScreenHarness test, long stamp, string session)
    {
        test.Profiles.UpdateState("prod", state => state.LastContinuation = new StoredContinuation
        {
            Continuation = JsonNode.Parse($$"""{ {{ScreenHarness.Continuation(stamp)}} }""")!["continuation"]!.DeepClone(),
            SessionId = session,
            ExpiresAt = test.Clock.Now.AddMinutes(2),
        });
    }
}
