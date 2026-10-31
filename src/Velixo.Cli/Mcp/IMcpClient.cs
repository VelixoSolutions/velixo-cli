using System.Text.Json.Nodes;
using Velixo.Cli.Config;

namespace Velixo.Cli.Mcp;

/// <summary>The CLI's connection to the Velixo MCP server. Every command reaches the server through it.</summary>
public interface IMcpClient
{
    /// <summary>Gets the MCP session id the client sends, or null before the first request.</summary>
    string? SessionId { get; }

    /// <summary>Gets the server's <c>serverInfo</c> from the <c>initialize</c> that opened the session.</summary>
    McpServerInfo? ServerInfo { get; }

    /// <summary>Lists every tool the server offers, following the <c>nextCursor</c> pages.</summary>
    Task<JsonArray> ListToolsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Calls <paramref name="name"/> with <paramref name="arguments"/> as given; the caller adds
    /// <c>intent</c>. The client adds <c>_meta.agent</c>.
    /// </summary>
    Task<McpToolResult> CallToolAsync(string name, JsonObject arguments, CancellationToken cancellationToken);
}
