using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Mcp;

namespace Velixo.Cli.Tests.Fakes;

/// <summary>Answers each tool call with the next scripted result, and records the calls.</summary>
internal sealed class FakeMcpClient : IMcpClient
{
    private readonly Queue<Func<CancellationToken, Task<McpToolResult>>> _results = new();

    public List<(string Name, JsonObject Arguments)> Calls { get; } = [];

    public string? SessionId => "session-1";

    public McpServerInfo? ServerInfo => new() { Name = "velixo-mcp-server", Version = "0.1.0" };

    public JsonArray Tools { get; set; } = [];

    public FakeMcpClient ThenText(string text, bool isError = false)
    {
        _results.Enqueue(_ => Task.FromResult(McpToolResult.FromText(text, isError)));
        return this;
    }

    /// <summary>Answers with the text <paramref name="text"/> returns when the call comes, so a test can look at the state then.</summary>
    public FakeMcpClient ThenText(Func<string> text)
    {
        _results.Enqueue(_ => Task.FromResult(McpToolResult.FromText(text())));
        return this;
    }

    public FakeMcpClient ThenHang()
    {
        _results.Enqueue(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    public Task<JsonArray> ListToolsAsync(CancellationToken cancellationToken) => Task.FromResult(Tools);

    public Task<McpToolResult> CallToolAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        Calls.Add((name, (JsonObject)arguments.DeepClone()));
        return _results.Count == 0
            ? throw new InvalidOperationException($"No scripted result for {name}.")
            : _results.Dequeue()(cancellationToken);
    }
}
