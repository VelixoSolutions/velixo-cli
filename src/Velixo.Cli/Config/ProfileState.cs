using System.Text.Json.Serialization;

namespace Velixo.Cli.Config;

/// <summary>
/// The contents of <c>state/&lt;profile&gt;.json</c>: what one command leaves for the next. Nothing
/// here is a credential, and losing the file costs a new MCP session at most.
/// </summary>
public sealed class ProfileState
{
    /// <summary>Gets or sets the MCP session id the server issued on <c>initialize</c>, sent by every later process.</summary>
    [JsonPropertyName("mcpSessionId")]
    public string? McpSessionId { get; set; }

    /// <summary>Gets or sets the MCP URL the session belongs to; a session is not sent to any other.</summary>
    [JsonPropertyName("mcpUrl")]
    public string? McpUrl { get; set; }

    [JsonPropertyName("server")]
    public McpServerInfo? Server { get; set; }

    [JsonPropertyName("lastContinuation")]
    public StoredContinuation? LastContinuation { get; set; }

    [JsonPropertyName("pendingLogin")]
    public PendingLogin? PendingLogin { get; set; }

    /// <summary>Gets or sets the conversation key the server minted for each conversation name, by that name.</summary>
    [JsonPropertyName("conversations")]
    public Dictionary<string, StoredConversation>? Conversations { get; set; }
}

/// <summary>The key the server minted for one conversation name, sent back on every later call with that name.</summary>
public sealed class StoredConversation
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the server endpoint that minted the key; the key is never sent to another.</summary>
    [JsonPropertyName("mcpUrl")]
    public string? McpUrl { get; set; }

    /// <summary>Gets or sets when the key was last sent or minted, refreshed at most hourly; the least recent is pruned first.</summary>
    [JsonPropertyName("usedAt")]
    public DateTimeOffset UsedAt { get; set; }
}
