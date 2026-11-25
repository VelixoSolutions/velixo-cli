using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Output;

namespace Velixo.Cli.Mcp;

/// <summary>
/// Takes out of every result and error message the <c>conversationKey</c> the server adds to a call that sends none.
/// The CLI sends no key, so the server adds one to every call; without this, it would print as a warning on every
/// command.
/// </summary>
public sealed class ConversationMcpClient(IMcpClient inner) : IMcpClient
{
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
        catch (CliException exception) when (ConversationKey.StripFrom(exception.Message) is { Key: not null } withoutKey)
        {
            throw exception.WithMessage(withoutKey.Message);
        }

        return Strip(result);
    }

    private static McpToolResult Strip(McpToolResult result)
    {
        if (!result.Texts.Any(text => ConversationKey.TryRead(text, out _)))
        {
            return result;
        }

        var content = new JsonArray();
        foreach (JsonNode? block in result.Content)
        {
            if (block is JsonObject { } text
                && (string?)text["type"] == "text"
                && (string?)text["text"] is { } value
                && ConversationKey.TryRead(value, out _))
            {
                continue;
            }

            content.Add(block?.DeepClone());
        }

        return new McpToolResult
        {
            IsError = result.IsError,
            Texts = result.Texts.Where(text => !ConversationKey.TryRead(text, out _)).ToList(),
            Content = content,
        };
    }
}
