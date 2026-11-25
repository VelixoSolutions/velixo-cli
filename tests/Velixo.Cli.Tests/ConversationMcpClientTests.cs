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

    [Theory]
    [InlineData(KeyBlock, true)]
    [InlineData("conversationKey: ck_1", true)]
    [InlineData("conversationKey: ck_1\nSend this key back on every later call.\n\nThanks.", true)]
    [InlineData("A warning that names conversationKey: ck_1 in passing.", false)]
    [InlineData("{\"conversationKey\":\"ck_1\"}", false)]
    public void Only_a_block_that_opens_with_the_key_reads_as_one(string text, bool isKey)
    {
        Assert.Equal(isKey, ConversationKey.TryRead(text, out _));
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

    private static McpToolResult McpResult(params string[] texts) => McpToolResult.From(new JsonObject
    {
        ["content"] = new JsonArray(texts.Select(text => (JsonNode?)new JsonObject { ["type"] = "text", ["text"] = text }).ToArray()),
    });
}
