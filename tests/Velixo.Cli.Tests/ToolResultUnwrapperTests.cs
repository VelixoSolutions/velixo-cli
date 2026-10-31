using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Tests;

public sealed class ToolResultUnwrapperTests
{
    [Fact]
    public void Json_textual_response_becomes_result_data_beside_the_other_fields()
    {
        var envelope = new JsonObject
        {
            ["taskId"] = "t_9a",
            ["status"] = "done",
            ["result"] = new JsonObject
            {
                ["textualResponse"] = """{"columns":[{"caption":"Customer"}],"rows":[["ABARTENDE"]],"totalCount":1}""",
                ["skippedFields"] = new JsonArray("Document.Hold"),
                ["continuation"] = new JsonObject { ["screenId"] = "SO301000", ["stamp"] = 17 },
            },
        };

        UnwrappedToolResult result = ToolResultUnwrapper.Unwrap(McpToolResult.FromText(envelope.ToJsonString()));

        Assert.Equal("t_9a", result.TaskId);
        Assert.Equal("done", result.Status);
        Assert.Equal(1, (int?)result.Data!["totalCount"]);
        Assert.Equal("Document.Hold", (string?)result.Document["result"]!["skippedFields"]![0]);
        Assert.Equal("SO301000", (string?)result.Document["result"]!["continuation"]!["screenId"]);
        Assert.False(result.Document["result"]!.AsObject().ContainsKey("textualResponse"));
    }

    [Fact]
    public void Non_json_textual_response_becomes_result_text()
    {
        const string envelope = """{"taskId":"t_1","status":"done","result":{"textualResponse":"Saved the order."}}""";

        UnwrappedToolResult result = ToolResultUnwrapper.Unwrap(McpToolResult.FromText(envelope));

        Assert.Equal("Saved the order.", result.Text);
        Assert.Null(result.Data);
    }

    [Fact]
    public void A_tool_without_a_task_has_its_json_under_result_data()
    {
        UnwrappedToolResult result = ToolResultUnwrapper.Unwrap(McpToolResult.FromText("""[{"name":"gab-dev","status":"ok"}]"""));

        Assert.Null(result.Status);
        Assert.Equal("gab-dev", (string?)result.Data![0]!["name"]);
    }

    [Fact]
    public void A_coded_screen_error_is_exit_5_with_its_code()
    {
        const string text = "SCREEN_SESSION_LOST: the screen session that held the open record is gone.";

        CliException error = Assert.Throws<CliException>(() => ToolResultUnwrapper.Unwrap(McpToolResult.FromText(text, isError: true)));

        Assert.Equal(ExitCodes.ServerRefused, error.ExitCode);
        Assert.Equal("SCREEN_SESSION_LOST", error.Code);
        Assert.Equal(text, error.Message);
        Assert.NotNull(error.NextStep);
    }

    [Fact]
    public void A_builder_error_plan_is_exit_3()
    {
        const string envelope = """{"taskId":"t_5c","status":"done","result":{"textualResponse":"ERROR: Which inquiry do you mean: AR-Aging or AR-Open?"}}""";

        CliException error = Assert.Throws<CliException>(() => ToolResultUnwrapper.Unwrap(McpToolResult.FromText(envelope)));

        Assert.Equal(ExitCodes.BuilderRefusal, error.ExitCode);
        Assert.Equal(ErrorCodes.BuilderRefusal, error.Code);
        Assert.StartsWith("ERROR: Which inquiry", error.Message, StringComparison.Ordinal);
        Assert.Equal("t_5c", error.TaskId);
    }

    [Fact]
    public void A_failed_task_whose_error_is_a_builder_question_is_exit_3_with_the_text_verbatim()
    {
        const string envelope = """{"taskId":"t_5d","status":"failed","error":"  ERROR: Which screen do you mean: Sales Orders or Quotes?\n"}""";

        CliException error = Assert.Throws<CliException>(() => ToolResultUnwrapper.Unwrap(McpToolResult.FromText(envelope, isError: true)));

        Assert.Equal(ExitCodes.BuilderRefusal, error.ExitCode);
        Assert.Equal(ErrorCodes.BuilderRefusal, error.Code);
        Assert.Equal("ERROR: Which screen do you mean: Sales Orders or Quotes?", error.Message);
        Assert.StartsWith("Put the builder's question to the user in plain words", error.NextStep, StringComparison.Ordinal);
        Assert.Equal("t_5d", error.TaskId);
    }

    [Theory]
    [InlineData("IMPORT_COLUMNS_MISSING: the file does not have 'Customer Class'.", "IMPORT_COLUMNS_MISSING", "velixo guide import")]
    [InlineData("SCREEN_SESSION_LOST: the session is gone. Nothing in this call was executed.", "SCREEN_SESSION_LOST", ErrorGuidance.SessionLostNothingRanStep)]
    [InlineData("SCREEN_SESSION_LOST: the session is gone; whether any of this call ran is unknown.", "SCREEN_SESSION_LOST", ErrorGuidance.SessionLostUnknownStep)]
    [InlineData("SCREEN_SETS_HEADER_ON_CONTINUATION: a continuation sends no header.", "SCREEN_SETS_HEADER_ON_CONTINUATION", "names the set to remove or add")]
    public void A_failed_task_whose_error_starts_with_a_code_is_exit_5_with_that_code(string text, string code, string nextStep)
    {
        var envelope = new JsonObject { ["taskId"] = "t_7", ["status"] = "failed", ["error"] = text }.ToJsonString();

        CliException error = Assert.Throws<CliException>(() => ToolResultUnwrapper.Unwrap(McpToolResult.FromText(envelope, isError: true)));

        Assert.Equal(ExitCodes.ServerRefused, error.ExitCode);
        Assert.Equal(code, error.Code);
        Assert.Equal(text, error.Message);
        Assert.Contains(nextStep, error.NextStep, StringComparison.Ordinal);
        Assert.Equal("t_7", error.TaskId);
    }

    [Fact]
    public void A_failed_task_is_exit_6_with_the_detail()
    {
        const string envelope = """{"taskId":"t_2b","status":"failed","error":"Acumatica returned 400: Conversion failed."}""";

        CliException error = Assert.Throws<CliException>(() => ToolResultUnwrapper.Unwrap(McpToolResult.FromText(envelope, isError: true)));

        Assert.Equal(ExitCodes.TaskFailed, error.ExitCode);
        Assert.Equal("t_2b", error.TaskId);
        Assert.Equal("Acumatica returned 400: Conversion failed.", error.Detail);
    }

    [Fact]
    public void An_expired_task_is_exit_6()
    {
        const string envelope = """{"taskId":"t_x","status":"expired","message":"Task not found."}""";

        CliException error = Assert.Throws<CliException>(() => ToolResultUnwrapper.Unwrap(McpToolResult.FromText(envelope)));

        Assert.Equal(ExitCodes.TaskFailed, error.ExitCode);
        Assert.Equal(ErrorCodes.TaskExpired, error.Code);
    }

    [Theory]
    [InlineData("Velixo Intelligence could not identify you. Please sign out and sign back in.", ErrorCodes.NotSignedIn)]
    [InlineData("This user cannot use Velixo yet (no account). Before calling any other tool, tell them what it says.", ErrorCodes.AccessDenied)]
    [InlineData("Velixo Intelligence access is not currently permitted for your account. Contact your Velixo administrator.", ErrorCodes.AccessDenied)]
    public void Sign_in_and_access_refusals_are_exit_4(string text, string code)
    {
        CliException error = Assert.Throws<CliException>(() => ToolResultUnwrapper.Unwrap(McpToolResult.FromText(text, isError: true)));

        Assert.Equal(ExitCodes.AuthenticationRequired, error.ExitCode);
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public void Any_other_tool_error_is_exit_5_with_the_text()
    {
        CliException error = Assert.Throws<CliException>(() =>
            ToolResultUnwrapper.Unwrap(McpToolResult.FromText("Your connections could not be read at the moment.", isError: true)));

        Assert.Equal(ExitCodes.ServerRefused, error.ExitCode);
        Assert.Equal(ErrorCodes.ToolError, error.Code);
    }

    [Fact]
    public void A_running_envelope_is_kept_for_the_follower()
    {
        const string envelope = """{"taskId":"t_1","status":"running","progress":{"label":"Building","elapsedMs":1200,"stalledForMs":0}}""";

        UnwrappedToolResult result = ToolResultUnwrapper.Unwrap(McpToolResult.FromText(envelope));

        Assert.True(result.IsRunning);
        Assert.Equal("Building", (string?)result.Progress!["label"]);
    }
}
