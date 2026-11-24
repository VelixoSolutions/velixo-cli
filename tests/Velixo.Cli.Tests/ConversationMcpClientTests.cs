using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;
using Velixo.Cli.Tests.Screen;

namespace Velixo.Cli.Tests;

public sealed class ConversationMcpClientTests : IDisposable
{
    private const string Profile = "prod";
    private const string KeyBlock = "conversationKey: ck_abc.def\nSend this `conversationKey` on every later call in this chat.";

    private readonly TestServices _test = new();
    private readonly FakeMcpClient _inner = new();

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task Without_a_conversation_it_sends_neither_argument_and_drops_the_key_block()
    {
        _inner.Then(McpResult("""{"ok":true}""", KeyBlock));

        McpToolResult result = await Client(conversation: null).CallToolAsync("list_connections", new JsonObject { ["intent"] = "x" }, TestContext.Current.CancellationToken);

        Assert.Equal(["intent"], _inner.Calls[0].Arguments.Select(argument => argument.Key));
        Assert.Equal(["""{"ok":true}"""], result.Texts);
        Assert.Single(result.Content);
        Assert.Null(_test.Services.Profiles.LoadState(Profile).Conversations);
    }

    [Fact]
    public async Task A_conversation_sends_its_name_then_the_key_the_server_minted_for_it()
    {
        _inner.Then(McpResult("first", KeyBlock)).Then(McpResult("second"));
        ConversationMcpClient client = Client("Overdue invoices");
        var arguments = new JsonObject { ["intent"] = "x" };

        McpToolResult first = await client.CallToolAsync("execute_query", arguments, TestContext.Current.CancellationToken);
        McpToolResult second = await client.CallToolAsync("execute_query", arguments, TestContext.Current.CancellationToken);

        Assert.Equal("Overdue invoices", (string?)_inner.Calls[0].Arguments[ConversationKey.NameArgument]);
        Assert.False(_inner.Calls[0].Arguments.ContainsKey(ConversationKey.KeyArgument));
        Assert.Equal("ck_abc.def", (string?)_inner.Calls[1].Arguments[ConversationKey.KeyArgument]);
        Assert.Equal("Overdue invoices", (string?)_inner.Calls[1].Arguments[ConversationKey.NameArgument]);
        Assert.Equal(["first"], first.Texts);
        Assert.Equal(["second"], second.Texts);
        Assert.Equal(["intent"], arguments.Select(argument => argument.Key));
    }

    [Fact]
    public async Task Another_conversation_gets_its_own_key()
    {
        _inner.Then(McpResult("a", KeyBlock)).Then(McpResult("b"));

        await Client("Month-end close").CallToolAsync("execute_query", [], TestContext.Current.CancellationToken);
        await Client("Vendor cleanup").CallToolAsync("execute_query", [], TestContext.Current.CancellationToken);

        Assert.False(_inner.Calls[1].Arguments.ContainsKey(ConversationKey.KeyArgument));
    }

    [Fact]
    public async Task A_profile_keeps_the_keys_of_its_latest_conversations_only()
    {
        for (var index = 0; index <= ConversationMcpClient.MaxStoredConversations; index++)
        {
            _inner.Then(McpResult("ok", $"conversationKey: ck_{index}\nSend this `conversationKey` on every later call in this chat."));
            await Client($"chat {index}", new ManualClock(DateTimeOffset.UnixEpoch.AddMinutes(index))).CallToolAsync("get_status", [], TestContext.Current.CancellationToken);
        }

        Dictionary<string, StoredConversation> stored = _test.Services.Profiles.LoadState(Profile).Conversations!;
        Assert.Equal(ConversationMcpClient.MaxStoredConversations, stored.Count);
        Assert.DoesNotContain("chat 0", stored.Keys);
        Assert.Equal($"ck_{ConversationMcpClient.MaxStoredConversations}", stored[$"chat {ConversationMcpClient.MaxStoredConversations}"].Key);
    }

    [Fact]
    public async Task An_error_message_loses_the_key_lines_and_keeps_its_exit_code()
    {
        _inner.ThenThrow(new CliException(ExitCodes.Failure, ErrorCodes.Protocol, "Server error -32603: boom\n" + KeyBlock, "retry"));

        CliException thrown = await Assert.ThrowsAsync<CliException>(() => Client("x").CallToolAsync("execute_query", [], TestContext.Current.CancellationToken));

        Assert.Equal("Server error -32603: boom", thrown.Message);
        Assert.Equal(ExitCodes.Failure, thrown.ExitCode);
        Assert.Equal("retry", thrown.NextStep);
    }

    [Theory]
    [InlineData(KeyBlock, true)]
    [InlineData("conversationKey: ck_1", true)]
    [InlineData("A warning that names conversationKey: ck_1 in passing.", false)]
    [InlineData("{\"conversationKey\":\"ck_1\"}", false)]
    public void Only_a_whole_key_block_reads_as_a_key(string text, bool isKey)
    {
        Assert.Equal(isKey, ConversationKey.TryRead(text, out _));
    }

    [Fact]
    public async Task A_command_in_a_conversation_prints_no_key_warning()
    {
        _test.Services.McpClientFactory = services =>
            new ConversationMcpClient(_inner, services.Profiles, services.Settings.ProfileName, services.Settings.Conversation, TimeProvider.System);
        _inner.Then(McpResult("Thank you. Velixo R&D received the report (1). Do not submit it again.", KeyBlock));

        var exit = await _test.RunAsync(
            "feedback", "--tool", "none", "--category", "other", "--severity", "minor",
            "--attempted", "a", "--problem", "p", "--proposed-change", "c", "--conversation", "Feedback test");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("Feedback test", (string?)_inner.Calls[0].Arguments[ConversationKey.NameArgument]);
        Assert.DoesNotContain("conversationKey", _test.Console.StdErr + _test.Console.StdOut, StringComparison.Ordinal);
    }

    private ConversationMcpClient Client(string? conversation, TimeProvider? clock = null) =>
        new(_inner, _test.Services.Profiles, Profile, conversation, clock ?? TimeProvider.System);

    private static McpToolResult McpResult(params string[] texts) => McpToolResult.From(new JsonObject
    {
        ["content"] = new JsonArray(texts.Select(text => (JsonNode?)new JsonObject { ["type"] = "text", ["text"] = text }).ToArray()),
    });
}
