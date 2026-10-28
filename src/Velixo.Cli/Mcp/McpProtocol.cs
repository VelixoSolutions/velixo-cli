namespace Velixo.Cli.Mcp;

/// <summary>The constants of the MCP Streamable HTTP transport the CLI speaks.</summary>
public static class McpProtocol
{
    public const string Version = "2025-06-18";

    public const string SessionIdHeader = "Mcp-Session-Id";

    public const string ProtocolVersionHeader = "MCP-Protocol-Version";

    public const string JsonMediaType = "application/json";

    public const string EventStreamMediaType = "text/event-stream";

    public const string ClientName = "velixo-cli";

    public const string ClientTitle = "Velixo CLI";
}
