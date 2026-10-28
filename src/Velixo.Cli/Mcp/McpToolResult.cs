using System.Text.Json.Nodes;

namespace Velixo.Cli.Mcp;

/// <summary>A <c>CallToolResult</c>: its content blocks and whether the tool reported an error.</summary>
public sealed class McpToolResult
{
    public bool IsError { get; init; }

    /// <summary>Gets the text of every <c>text</c> content block, in order.</summary>
    required public IReadOnlyList<string> Texts { get; init; }

    /// <summary>Gets the whole <c>content</c> array, resource links included.</summary>
    required public JsonArray Content { get; init; }

    public static McpToolResult From(JsonNode? result)
    {
        JsonArray content = result?["content"] as JsonArray ?? [];
        var texts = content
            .OfType<JsonObject>()
            .Where(block => (string?)block["type"] == "text")
            .Select(block => (string?)block["text"] ?? string.Empty)
            .ToList();

        return new McpToolResult
        {
            IsError = result?["isError"] is JsonValue flag && flag.TryGetValue(out bool isError) && isError,
            Texts = texts,
            Content = (JsonArray)content.DeepClone(),
        };
    }

    public static McpToolResult FromText(string text, bool isError = false) => new()
    {
        IsError = isError,
        Texts = [text],
        Content = [new JsonObject { ["type"] = "text", ["text"] = text }],
    };
}
