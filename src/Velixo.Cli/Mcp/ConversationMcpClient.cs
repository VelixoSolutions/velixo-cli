using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Output;

namespace Velixo.Cli.Mcp;

/// <summary>
/// Sends an agent's conversation, and takes out of every result and error message the <c>conversationKey</c> the
/// server adds to a call that sends no valid one, so it never prints as a warning or at the end of an error.
/// </summary>
/// <remarks>
/// The agent keeps the key, as an MCP chat client does: the CLI stores nothing. With <c>--conversation-name</c> or
/// <c>--conversation-key</c>, every call sends them as <c>conversationName</c> and <c>conversationKey</c>, and a key the
/// server gives back, because the call sent none or one it refused, goes to <paramref name="announceKey"/> once, for the
/// agent to pass on its later calls. Without either, the call sends neither and the key is dropped.
/// </remarks>
public sealed class ConversationMcpClient(
    IMcpClient inner,
    string? conversationName = null,
    string? conversationKey = null,
    Action<string>? announceKey = null) : IMcpClient
{
    private readonly string? _name = string.IsNullOrWhiteSpace(conversationName) ? null : conversationName.Trim();
    private readonly string? _key = string.IsNullOrWhiteSpace(conversationKey) ? null : conversationKey.Trim();
    private string? _announced;

    /// <summary>The message of an error that held nothing but the key.</summary>
    public const string EmptyErrorMessage = "The server answered with an error and said nothing more.";

    public string? SessionId => inner.SessionId;

    public McpServerInfo? ServerInfo => inner.ServerInfo;

    public Task<JsonArray> ListToolsAsync(CancellationToken cancellationToken) => inner.ListToolsAsync(cancellationToken);

    public async Task<McpToolResult> CallToolAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        JsonObject sent = arguments;
        if (_name is not null || _key is not null)
        {
            sent = (JsonObject)arguments.DeepClone();
            if (_name is not null)
            {
                sent[ConversationKey.NameArgument] = _name;
            }

            if (_key is not null)
            {
                sent[ConversationKey.KeyArgument] = _key;
            }
        }

        McpToolResult result;
        try
        {
            result = await inner.CallToolAsync(name, sent, cancellationToken).ConfigureAwait(false);
        }
        catch (CliException exception) when (ConversationKey.Strip(exception.Message) is { HadKey: true } withoutKey)
        {
            Announce(withoutKey.Key);
            throw exception.WithMessage(withoutKey.Text.Length > 0 ? withoutKey.Text : EmptyErrorMessage);
        }

        return Strip(result);
    }

    /// <summary>Hands a key the server gave to the agent, once, when the agent takes part in a conversation.</summary>
    private void Announce(string? key)
    {
        if (key is null || announceKey is null || (_name is null && _key is null) || key == _announced)
        {
            return;
        }

        _announced = key;
        announceKey(key);
    }

    private McpToolResult Strip(McpToolResult result)
    {
        if (!result.Texts.Any(text => ConversationKey.Strip(text).HadKey))
        {
            return result;
        }

        var content = new JsonArray();
        var texts = new List<string>();
        foreach (JsonNode? node in result.Content)
        {
            if (node is JsonObject { } block && (string?)block["type"] == "text")
            {
                (var text, var hadKey, var key) = ConversationKey.Strip((string?)block["text"] ?? string.Empty);
                Announce(key);
                if (hadKey && text.Length == 0)
                {
                    continue;
                }

                var kept = (JsonObject)block.DeepClone();
                kept["text"] = text;
                content.Add((JsonNode)kept);
                texts.Add(text);
                continue;
            }

            content.Add(node?.DeepClone());
        }

        return new McpToolResult { IsError = result.IsError, Texts = texts, Content = content };
    }
}
