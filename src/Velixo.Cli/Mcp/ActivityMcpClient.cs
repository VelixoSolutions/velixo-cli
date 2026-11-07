using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Output;

namespace Velixo.Cli.Mcp;

/// <summary>
/// Shows a spinner while a call to the server is in flight, so a slow network or a sign-in refresh does not look
/// like a hang. Inside a longer activity, such as a task the follower waits on, it shows nothing of its own.
/// </summary>
public sealed class ActivityMcpClient(IMcpClient inner, OutputWriter output) : IMcpClient
{
    /// <summary>What the spinner says while a call is in flight.</summary>
    public const string Label = "Waiting for Velixo";

    public string? SessionId => inner.SessionId;

    public McpServerInfo? ServerInfo => inner.ServerInfo;

    public async Task<JsonArray> ListToolsAsync(CancellationToken cancellationToken)
    {
        using Activity activity = output.BeginActivity(Label);
        return await inner.ListToolsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<McpToolResult> CallToolAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        using Activity activity = output.BeginActivity(Label);
        return await inner.CallToolAsync(name, arguments, cancellationToken).ConfigureAwait(false);
    }
}
