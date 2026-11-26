using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class ConversationMcpClientTests : IDisposable
{
    private const string KeyBlock = "conversationKey: ck_abc.def\nSend this `conversationKey` on every later call in this chat.";

    private readonly TestServices _test = new();
    private readonly FakeMcpClient _inner = new();

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task The_key_block_leaves_the_result_and_the_arguments_go_through_unchanged()
    {
        _inner.Then(McpResult("""{"ok":true}""", KeyBlock));

        McpToolResult result = await new ConversationMcpClient(_inner)
            .CallToolAsync("list_connections", new JsonObject { ["intent"] = "x" }, TestContext.Current.CancellationToken);

        Assert.Equal(["intent"], _inner.Calls[0].Arguments.Select(argument => argument.Key));
        Assert.Equal(["""{"ok":true}"""], result.Texts);
        Assert.Single(result.Content);
    }

    [Fact]
    public async Task A_result_without_a_key_is_returned_as_it_came()
    {
        McpToolResult sent = McpResult("data", "A real warning.");
        _inner.Then(sent);

        McpToolResult result = await new ConversationMcpClient(_inner).CallToolAsync("execute_query", [], TestContext.Current.CancellationToken);

        Assert.Same(sent, result);
    }

    [Fact]
    public async Task An_error_message_loses_the_key_lines_and_keeps_its_exit_code()
    {
        _inner.ThenThrow(new CliException(ExitCodes.Failure, ErrorCodes.Protocol, "Server error -32603: boom\n" + KeyBlock, "retry"));

        CliException thrown = await Assert.ThrowsAsync<CliException>(
            () => new ConversationMcpClient(_inner).CallToolAsync("execute_query", [], TestContext.Current.CancellationToken));

        Assert.Equal("Server error -32603: boom", thrown.Message);
        Assert.Equal(ExitCodes.Failure, thrown.ExitCode);
        Assert.Equal("retry", thrown.NextStep);
    }

    [Fact]
    public async Task An_error_result_with_the_key_in_its_text_keeps_the_error_alone()
    {
        _inner.Then(McpToolResult.FromText("Several ERP connections are usable.\n" + KeyBlock, isError: true));

        McpToolResult result = await new ConversationMcpClient(_inner).CallToolAsync("execute_query", [], TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(["Several ERP connections are usable."], result.Texts);
    }

    [Fact]
    public async Task What_follows_the_key_in_the_same_block_is_kept()
    {
        _inner.Then(McpResult(KeyBlock + "\n\nYou have 3 credits left."));

        McpToolResult result = await new ConversationMcpClient(_inner).CallToolAsync("execute_query", [], TestContext.Current.CancellationToken);

        Assert.Equal(["You have 3 credits left."], result.Texts);
    }

    [Fact]
    public async Task An_error_that_was_only_the_key_still_says_something()
    {
        _inner.ThenThrow(new CliException(ExitCodes.Usage, ErrorCodes.Usage, KeyBlock));

        CliException thrown = await Assert.ThrowsAsync<CliException>(
            () => new ConversationMcpClient(_inner).CallToolAsync("execute_query", [], TestContext.Current.CancellationToken));

        Assert.Equal(ConversationMcpClient.EmptyErrorMessage, thrown.Message);
    }

    [Theory]
    [InlineData(KeyBlock, "")]
    [InlineData("conversationKey: ck_1", "")]
    [InlineData("conversationKey: ck_1 (send it back on every call)", "")]
    [InlineData("conversationKey: ck_1\nSend this key back on every later call.", "Send this key back on every later call.")]
    [InlineData("A warning that names conversationKey: ck_1 in passing.", "A warning that names conversationKey: ck_1 in passing.")]
    [InlineData("{\"conversationKey\":\"ck_1\"}", "{\"conversationKey\":\"ck_1\"}")]
    public void Only_a_line_that_opens_with_the_key_is_removed(string text, string expected)
    {
        Assert.Equal(expected, ConversationKey.Strip(text).Text);
    }

    [Fact]
    public async Task A_command_prints_no_key_warning()
    {
        _test.Services.McpClientFactory = _ => new ConversationMcpClient(_inner);
        _inner.Then(McpResult("Thank you. Velixo R&D received the report (1). Do not submit it again.", KeyBlock));

        var exit = await _test.RunAsync(
            "feedback", "--tool", "none", "--category", "other", "--severity", "minor",
            "--attempted", "a", "--problem", "p", "--proposed-change", "c");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("conversationKey", _test.Console.StdErr + _test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_without_a_key_is_sent_and_the_key_the_server_gives_is_announced_once()
    {
        var announced = new List<string>();
        _inner.Then(McpResult("first", KeyBlock)).Then(McpResult("second", KeyBlock));
        var client = new ConversationMcpClient(_inner, "Overdue invoices", null, announced.Add);
        var arguments = new JsonObject { ["intent"] = "x" };

        McpToolResult first = await client.CallToolAsync("execute_query", arguments, TestContext.Current.CancellationToken);
        await client.CallToolAsync("get_status", arguments, TestContext.Current.CancellationToken);

        Assert.Equal("Overdue invoices", (string?)_inner.Calls[0].Arguments[ConversationKey.NameArgument]);
        Assert.False(_inner.Calls[0].Arguments.ContainsKey(ConversationKey.KeyArgument));
        Assert.Equal(["ck_abc.def"], announced);
        Assert.Equal(["first"], first.Texts);
        Assert.Equal(["intent"], arguments.Select(argument => argument.Key));
    }

    [Fact]
    public async Task A_key_the_server_accepts_is_sent_and_nothing_is_announced()
    {
        var announced = new List<string>();
        _inner.Then(McpResult("ok"));

        await new ConversationMcpClient(_inner, "Overdue invoices", "ck_abc.def", announced.Add)
            .CallToolAsync("execute_query", [], TestContext.Current.CancellationToken);

        Assert.Equal("ck_abc.def", (string?)_inner.Calls[0].Arguments[ConversationKey.KeyArgument]);
        Assert.Equal("Overdue invoices", (string?)_inner.Calls[0].Arguments[ConversationKey.NameArgument]);
        Assert.Empty(announced);
    }

    [Fact]
    public async Task A_key_the_server_refuses_is_replaced_by_the_one_it_announces()
    {
        var announced = new List<string>();
        _inner.Then(McpResult("ok", "conversationKey: ck_new\nSend this `conversationKey` on every later call in this chat."));

        await new ConversationMcpClient(_inner, null, "ck_stale", announced.Add)
            .CallToolAsync("execute_query", [], TestContext.Current.CancellationToken);

        Assert.Equal(["ck_new"], announced);
    }

    [Fact]
    public async Task The_key_of_a_failed_call_is_announced_too()
    {
        var announced = new List<string>();
        _inner.ThenThrow(new CliException(ExitCodes.Failure, ErrorCodes.Protocol, "Server error -32603: boom\n" + KeyBlock));

        await Assert.ThrowsAsync<CliException>(() => new ConversationMcpClient(_inner, "Overdue invoices", null, announced.Add)
            .CallToolAsync("execute_query", [], TestContext.Current.CancellationToken));

        Assert.Equal(["ck_abc.def"], announced);
    }

    [Fact]
    public async Task Without_a_conversation_nothing_is_sent_or_announced()
    {
        var announced = new List<string>();
        _inner.Then(McpResult("ok", KeyBlock));

        await new ConversationMcpClient(_inner, null, null, announced.Add).CallToolAsync("execute_query", [], TestContext.Current.CancellationToken);

        Assert.Empty(_inner.Calls[0].Arguments);
        Assert.Empty(announced);
    }

    [Fact]
    public async Task A_command_with_a_conversation_name_prints_the_key_on_stderr_even_when_quiet()
    {
        _test.Services.McpClientFactory = services => new ConversationMcpClient(
            _inner,
            services.Settings.ConversationName,
            services.Settings.ConversationKey,
            key => services.Output.Note($"Conversation key: {key}. Pass --conversation-key {key} on every later call of this conversation."));
        _inner.Then(McpResult("Thank you. Velixo R&D received the report (1). Do not submit it again.", KeyBlock));

        var exit = await _test.RunAsync(
            "feedback", "--tool", "none", "--category", "other", "--severity", "minor",
            "--attempted", "a", "--problem", "p", "--proposed-change", "c", "--conversation-name", "Feedback test", "--quiet");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("ck_abc.def", _test.Console.StdOut, StringComparison.Ordinal);
        Assert.Contains("Pass --conversation-key ck_abc.def", _test.Console.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("warning", _test.Console.StdErr, StringComparison.Ordinal);
    }

    private static McpToolResult McpResult(params string[] texts) => McpToolResult.From(new JsonObject
    {
        ["content"] = new JsonArray(texts.Select(text => (JsonNode?)new JsonObject { ["type"] = "text", ["text"] = text }).ToArray()),
    });
}
