using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Output;

namespace Velixo.Cli.Mcp;

/// <summary>
/// Takes out of every result and error message the <c>conversationKey</c> the server adds to a call that sends none.
/// The CLI sends no key, so the server adds one to every call; without this, it would print as a warning on every
/// command, or at the end of every error.
/// </summary>
public sealed class ConversationMcpClient(IMcpClient inner) : IMcpClient
{
    /// <summary>The message of an error that held nothing but the key.</summary>
    public const string EmptyErrorMessage = "The server answered with an error and said nothing more.";

    public string? SessionId => inner.SessionId;

    public McpServerInfo? ServerInfo => inner.ServerInfo;

    public Task<JsonArray> ListToolsAsync(CancellationToken cancellationToken) => inner.ListToolsAsync(cancellationToken);

    public async Task<McpToolResult> CallToolAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        McpToolResult result;
        try
        {
            result = await inner.CallToolAsync(name, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (CliException exception) when (ConversationKey.Strip(exception.Message) is { HadKey: true } withoutKey)
        {
            throw exception.WithMessage(withoutKey.Text.Length > 0 ? withoutKey.Text : EmptyErrorMessage);
        }

        return Strip(result);
    }

    private static McpToolResult Strip(McpToolResult result)
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
                (var text, var hadKey) = ConversationKey.Strip((string?)block["text"] ?? string.Empty);
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
