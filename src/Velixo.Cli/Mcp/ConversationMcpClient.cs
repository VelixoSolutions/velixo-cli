using System.Text.Json.Nodes;
using Velixo.Cli.Config;

namespace Velixo.Cli.Mcp;

/// <summary>
/// Ties the calls of one chat together in the server's audit. With a conversation name (<c>--conversation</c> or
/// <c>VELIXO_CONVERSATION</c>), every call sends it as <c>conversationName</c>, with the key the server minted for
/// that name on an earlier call, kept in <c>state/&lt;profile&gt;.json</c>, as <c>conversationKey</c>. Without one,
/// it sends neither. Either way it takes the key block out of the result, so it never prints as a warning.
/// </summary>
public sealed class ConversationMcpClient(
    IMcpClient inner,
    IProfileStore profiles,
    string profile,
    Uri mcpUrl,
    string? conversation,
    TimeProvider clock) : IMcpClient
{
    /// <summary>How many conversations a profile keeps a key for; the least recently used go first.</summary>
    public const int MaxStoredConversations = 20;

    /// <summary>How stale a key's last use may be before sending it writes the state again.</summary>
    public static readonly TimeSpan UseRefresh = TimeSpan.FromHours(1);

    public string? SessionId => inner.SessionId;

    public McpServerInfo? ServerInfo => inner.ServerInfo;

    public Task<JsonArray> ListToolsAsync(CancellationToken cancellationToken) => inner.ListToolsAsync(cancellationToken);

    public async Task<McpToolResult> CallToolAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var named = string.IsNullOrWhiteSpace(conversation) ? null : conversation.Trim();
        JsonObject sent = arguments;
        if (named is not null)
        {
            sent = (JsonObject)arguments.DeepClone();
            sent[ConversationKey.NameArgument] = named;
            if (StoredKey(named) is { } stored)
            {
                sent[ConversationKey.KeyArgument] = stored.Key;
                if (clock.GetUtcNow() - stored.UsedAt >= UseRefresh)
                {
                    Store(named, stored.Key);
                }
            }
        }

        McpToolResult result;
        try
        {
            result = await inner.CallToolAsync(name, sent, cancellationToken).ConfigureAwait(false);
        }
        catch (Output.CliException exception) when (ConversationKey.StripFrom(exception.Message) is { Key: { } errorKey } withoutKey)
        {
            if (named is not null)
            {
                Store(named, errorKey);
            }

            throw exception.WithMessage(withoutKey.Message);
        }

        (McpToolResult stripped, string? minted) = Strip(result);
        if (named is not null && minted is not null)
        {
            Store(named, minted);
        }

        return stripped;
    }

    private static (McpToolResult Result, string? Key) Strip(McpToolResult result)
    {
        string? key = null;
        var content = new JsonArray();
        foreach (JsonNode? block in result.Content)
        {
            if (block is JsonObject { } text
                && (string?)text["type"] == "text"
                && (string?)text["text"] is { } value
                && ConversationKey.TryRead(value, out var found))
            {
                key = found;
                continue;
            }

            content.Add(block?.DeepClone());
        }

        if (key is null)
        {
            return (result, null);
        }

        return (new McpToolResult
        {
            IsError = result.IsError,
            Texts = result.Texts.Where(text => !ConversationKey.TryRead(text, out _)).ToList(),
            Content = content,
        }, key);
    }

    private StoredConversation? StoredKey(string named)
    {
        ProfileState state = profiles.LoadState(profile);
        return state.Conversations is { } conversations
            && conversations.TryGetValue(named, out StoredConversation? stored)
            && stored.McpUrl == mcpUrl.ToString()
            ? stored
            : null;
    }

    private void Store(string named, string key)
    {
        try
        {
            profiles.UpdateState(profile, state =>
            {
                Dictionary<string, StoredConversation> conversations = state.Conversations ??= new(StringComparer.Ordinal);
                conversations[named] = new StoredConversation { Key = key, McpUrl = mcpUrl.ToString(), UsedAt = clock.GetUtcNow() };
                foreach (var oldest in conversations
                    .OrderByDescending(pair => pair.Value.UsedAt)
                    .Skip(MaxStoredConversations)
                    .Select(pair => pair.Key)
                    .ToList())
                {
                    conversations.Remove(oldest);
                }
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Losing the key costs the audit one link between two calls, never the call itself: a call that wrote
            // to the ERP must not end in an error that invites a retry. A locked or read-only state file lands here.
        }
    }
}
