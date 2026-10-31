using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Tests.Screen;

public sealed class ScreenRunTests
{
    [Fact]
    public async Task Sends_the_header_rows_and_finish_with_the_bound_variables()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var rows = test.Write("rows.json", """[{"values":{"InventoryID":"AALEGO500","Qty":"2"}}]""");
        test.Mcp.ThenText(ScreenHarness.Done(beside: "\"feedback\":{\"record\":{\"OrderNbr\":\"SO007231\"}}"));

        var exit = await test.RunAsync(
            "command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--param-null", "Note", "--rows", rows,
            "--connection", "gab-dev", "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        JsonObject sent = test.Executed;
        Assert.Equal(ScreenHarness.Intent, (string?)sent["intent"]);
        Assert.Equal("SO301000", (string?)sent["target"]);
        Assert.Equal("gab-dev", (string?)sent["connectionName"]);
        Assert.Equal(3, sent["commands"]!.AsArray().Count);
        Assert.Equal(2, sent["lineCommands"]!.AsArray().Count);
        Assert.Equal(2, sent["finishCommands"]!.AsArray().Count);
        Assert.Equal("ACME", (string?)sent["variables"]!["CustomerID"]);
        Assert.True(sent["variables"]!.AsObject().ContainsKey("Note"));
        Assert.Null(sent["variables"]!["Note"]);
        Assert.Equal("AALEGO500", (string?)sent["rows"]![0]!["values"]!["InventoryID"]);
        Assert.False(sent.ContainsKey("leaveOpen"));
        Assert.False(sent.ContainsKey("continuation"));
        Assert.Equal("SO007231", (string?)test.Output["result"]!["feedback"]!["record"]!["OrderNbr"]);
        Assert.True(((JsonObject)test.Output["result"]!).ContainsKey("continuation"));
    }

    [Fact]
    public async Task Lists_every_unbound_placeholder_and_unknown_key_before_any_call()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var rows = test.Write("rows.json", """[{"values":{"InventoryID":"AALEGO500","Colour":"red"}},{"values":{"InventoryID":"X","Qty":"1"}}]""");

        var exit = await test.RunAsync(
            "command", "run", "--batch", batch, "--param", "Note=x", "--param", "Region=EU", "--param", "Qty=3", "--rows", rows,
            "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal(ErrorCodes.ScreenVariableMissing, (string?)test.Error["code"]);
        var message = (string?)test.Error["message"];
        Assert.StartsWith("SCREEN_VARIABLE_MISSING: ", message, StringComparison.Ordinal);
        Assert.Contains("'CustomerID'", message, StringComparison.Ordinal);
        Assert.Contains("'Region'", message, StringComparison.Ordinal);
        Assert.Contains("own value for 'Qty'", message, StringComparison.Ordinal);
        Assert.Contains("line 1 needs 'Qty'", message, StringComparison.Ordinal);
        Assert.Contains("line 1 carries 'Colour'", message, StringComparison.Ordinal);
        Assert.DoesNotContain("line 2", message, StringComparison.Ordinal);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task Only_unknown_keys_are_SCREEN_VARIABLE_UNKNOWN()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--param", "customerid=acme", "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal(ErrorCodes.ScreenVariableUnknown, (string?)test.Error["code"]);
        Assert.Contains("'customerid'", (string?)test.Error["message"], StringComparison.Ordinal);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task Rows_for_a_batch_without_lineCommands_is_refused_locally_with_the_server_code()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        var rows = test.Write("rows.json", "[]");

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--rows", rows, "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal(ErrorCodes.ScreenSetsRowsWithoutLine, (string?)test.Error["code"]);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task LineCommands_without_rows_is_refused_locally_with_the_server_code()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--param", "Note=", "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.ServerRefused, exit);
        Assert.Equal(ErrorCodes.ScreenSetsLineWithoutRows, (string?)test.Error["code"]);
        Assert.Contains("[]", (string?)test.Error["nextStep"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dry_run_prints_the_summary_document_and_calls_nothing()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("order.batch.json", ScreenHarness.OrderBatch);
        var rows = test.Write("rows.json", """[{"values":{"InventoryID":"A","Qty":"1"}},{"values":{"InventoryID":"B","Qty":"2"}}]""");

        var exit = await test.RunAsync(
            "command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--param", "Note=", "--rows", rows, "--dry-run", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        JsonNode document = test.Output;
        Assert.True((bool)document["dryRun"]!);
        Assert.Equal("SO301000", (string?)document["target"]);
        Assert.Equal(3 + 2 + (2 * 2), (int)document["commandCount"]!);
        Assert.Equal(string.Empty, (string?)document["variables"]!["Note"]);
        Assert.Equal(2, (int)document["rows"]!);
        Assert.Empty(document["attachments"]!.AsArray());
        Assert.True((bool)document["wouldWrite"]!);
        Assert.Contains("Note = \"\" (writes a blank)", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_batch_read_from_stdin_runs_like_one_from_a_file()
    {
        var data = JsonNode.Parse(ScreenHarness.OrderBatch)!["result"]!["data"]!.ToJsonString();
        using var test = new ScreenHarness(input: data);
        var rows = test.Write("rows.json", """[{"values":{"InventoryID":"A","Qty":"1"}}]""");

        var exit = await test.RunAsync(
            "command", "run", "--batch", "-", "--param", "CustomerID=ACME", "--param", "Note=", "--rows", rows, "--dry-run", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("SO301000", (string?)test.Output["target"]);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_batch_on_stdin_that_is_not_json_is_exit_2()
    {
        using var test = new ScreenHarness(input: "not json");

        var exit = await test.RunAsync("command", "run", "--batch", "-", "--dry-run", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("The batch on stdin is not valid JSON", (string?)test.Error["message"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dry_run_of_a_read_only_batch_would_not_write()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("read.batch.json", ScreenHarness.ReadBatch);

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--dry-run", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False((bool)test.Output["wouldWrite"]!);
    }

    [Fact]
    public async Task A_write_without_a_terminal_or_yes_requires_confirmation()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(ErrorCodes.ConfirmationRequired, (string?)test.Error["code"]);
        Assert.Contains("--yes", (string?)test.Error["nextStep"], StringComparison.Ordinal);
        Assert.Contains("Screen: AR301000", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("CustomerID = \"ACME\"", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_write_declined_on_a_terminal_does_not_run()
    {
        using var test = new ScreenHarness(terminal: true, input: "n\n");
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("Run this batch? [y/N]", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains($"[{ErrorCodes.ConfirmationDeclined}]", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Empty(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_write_accepted_on_a_terminal_runs()
    {
        using var test = new ScreenHarness(terminal: true, input: "y\n");
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        test.Mcp.ThenText(ScreenHarness.Done());

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Run this batch? [y/N]", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Single(test.Mcp.Calls);
    }

    [Fact]
    public async Task Yes_skips_the_question_on_a_terminal()
    {
        using var test = new ScreenHarness(terminal: true);
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        test.Mcp.ThenText(ScreenHarness.Done());

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("Run this batch?", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Single(test.Mcp.Calls);
    }

    [Fact]
    public async Task A_read_only_batch_runs_without_confirmation()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("read.batch.json", ScreenHarness.ReadBatch);
        test.Mcp.ThenText(ScreenHarness.Done());

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(test.Executed.ContainsKey("variables"));
    }

    [Fact]
    public async Task An_action_the_ERP_refused_is_exit_6_with_its_sentence()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        test.Mcp.ThenText(
            """{"taskId":"t_9","status":"failed","error":"Acumatica answered: The Release button is disabled. Feedback: {\"record\":{}} (task id: t_9)"}""",
            isError: true);

        var exit = await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--yes", "--intent", ScreenHarness.Intent);

        Assert.Equal(ExitCodes.TaskFailed, exit);
        Assert.Equal(ErrorCodes.ErpActionRefused, (string?)test.Error["code"]);
        Assert.Contains("The Release button is disabled.", (string?)test.Error["detail"], StringComparison.Ordinal);
        Assert.StartsWith("Do not run the batch again", (string?)test.Error["nextStep"], StringComparison.Ordinal);
    }
}
