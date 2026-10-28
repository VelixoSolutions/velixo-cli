using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Velixo.Cli.Config;

/// <summary>
/// The record the last <c>command run --leave-open</c> left open, with the MCP session it lives in.
/// A continuation is only good in that session and only until <see cref="ExpiresAt"/>.
/// </summary>
public sealed class StoredContinuation
{
    /// <summary>Gets or sets the <c>continuation</c> object exactly as the server returned it.</summary>
    [JsonPropertyName("continuation")]
    public JsonNode? Continuation { get; set; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }
}
