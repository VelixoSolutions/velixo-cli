using System.Text.Json.Nodes;

namespace Velixo.Cli.Mcp;

/// <summary>
/// A tool result as the CLI prints it. A task tool's document keeps the server's
/// <c>{ taskId, status, result }</c> envelope with <c>result.textualResponse</c> parsed into
/// <c>result.data</c>, or kept as <c>result.text</c> when it is not JSON. Any other tool's document is
/// <c>{ result: { data } }</c> or <c>{ result: { text } }</c>.
/// </summary>
public sealed class UnwrappedToolResult
{
    required public JsonObject Document { get; init; }

    public string? TaskId { get; init; }

    /// <summary>Gets the task status, or null for a tool that does not run a task.</summary>
    public string? Status { get; init; }

    public bool IsRunning => Status == TaskStatuses.Running;

    /// <summary>Gets the parsed result, or null when the tool answered with plain text.</summary>
    public JsonNode? Data => Document["result"]?["data"];

    /// <summary>Gets the plain-text result, or null when it parsed as JSON.</summary>
    public string? Text => (string?)Document["result"]?["text"];

    /// <summary>Gets the <c>progress</c> object of a running task.</summary>
    public JsonObject? Progress => Document["progress"] as JsonObject;
}
