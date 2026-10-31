using System.Net;
using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class StreamableHttpMcpClientTests : IDisposable
{
    private const string Profile = "nx-dev";

    private static readonly Uri _endpoint = new("https://nx-dev.velixo.com/mcp");

    private readonly TempDirectory _directory = new();
    private readonly FakeHttpHandler _http = new();
    private readonly FakeAccessTokens _tokens = new();
    private readonly ProfileStore _profiles;

    public StreamableHttpMcpClientTests()
    {
        _profiles = new ProfileStore(new ConfigPaths(_directory.Path));
    }

    [Fact]
    public async Task Initialize_stores_the_session_id_and_sends_it_from_then_on()
    {
        _http.ThenJson(InitializeResult, sessionId: "session-A")
            .ThenStatus(HttpStatusCode.Accepted)
            .ThenJson(ToolResult(1 + 1, "[]"));

        McpToolResult result = await Client("claude-code").CallToolAsync(McpToolNames.ListConnections, [], TestContext.Current.CancellationToken);

        Assert.Equal("[]", result.Texts[0]);
        Assert.Equal("session-A", _profiles.LoadState(Profile).McpSessionId);
        Assert.Equal("0.1.0", _profiles.LoadState(Profile).Server?.Version);

        RecordedRequest initialize = _http.Requests[0];
        JsonNode initializeBody = JsonNode.Parse(initialize.Body!)!;
        Assert.Equal("initialize", (string?)initializeBody["method"]);
        Assert.Equal("2025-06-18", (string?)initializeBody["params"]!["protocolVersion"]);
        Assert.Equal("velixo-cli", (string?)initializeBody["params"]!["clientInfo"]!["name"]);
        Assert.Null(initialize.Header("Mcp-Session-Id"));
        Assert.Equal("Bearer access-1", initialize.Header("Authorization"));
        Assert.Equal("2025-06-18", initialize.Header("MCP-Protocol-Version"));
        Assert.Contains("text/event-stream", initialize.Header("Accept"), StringComparison.Ordinal);

        Assert.Equal("notifications/initialized", (string?)JsonNode.Parse(_http.Requests[1].Body!)!["method"]);
        Assert.Equal("session-A", _http.Requests[1].Header("Mcp-Session-Id"));

        RecordedRequest call = _http.Requests[2];
        Assert.Equal("session-A", call.Header("Mcp-Session-Id"));
        JsonNode callBody = JsonNode.Parse(call.Body!)!;
        Assert.Equal("tools/call", (string?)callBody["method"]);
        Assert.Equal("claude-code", (string?)callBody["params"]!["_meta"]!["agent"]);
    }

    [Fact]
    public async Task A_stored_session_id_is_sent_without_initializing()
    {
        StoreSession("session-stored");
        _http.ThenJson(ToolResult(1, "[]"));

        await Client().CallToolAsync(McpToolNames.ListConnections, [], TestContext.Current.CancellationToken);

        RecordedRequest only = Assert.Single(_http.Requests);
        Assert.Equal("session-stored", only.Header("Mcp-Session-Id"));
        JsonNode body = JsonNode.Parse(only.Body!)!;
        Assert.Equal("tools/call", (string?)body["method"]);
        Assert.True(body["params"]!["_meta"]!.AsObject().ContainsKey("agent"));
        Assert.Null(body["params"]!["_meta"]!["agent"]);
    }

    [Fact]
    public async Task A_404_on_a_stored_session_initializes_again_and_retries_once()
    {
        StoreSession("session-old");
        _http.ThenStatus(HttpStatusCode.NotFound)
            .ThenJson(InitializeResult, sessionId: "session-new")
            .ThenStatus(HttpStatusCode.Accepted)
            .ThenJson(ToolResult(3, "[\"ok\"]"));

        var client = Client();
        McpToolResult result = await client.CallToolAsync(McpToolNames.ListConnections, [], TestContext.Current.CancellationToken);

        Assert.Equal("[\"ok\"]", result.Texts[0]);
        Assert.Equal(4, _http.Requests.Count);
        Assert.Equal("session-old", _http.Requests[0].Header("Mcp-Session-Id"));
        Assert.Equal("session-new", _http.Requests[3].Header("Mcp-Session-Id"));
        Assert.Equal("session-new", _profiles.LoadState(Profile).McpSessionId);
        Assert.True(client.SessionWasReplaced);
    }

    [Fact]
    public async Task A_second_404_after_initializing_fails_instead_of_looping()
    {
        StoreSession("session-old");
        _http.ThenStatus(HttpStatusCode.NotFound)
            .ThenJson(InitializeResult, sessionId: "session-new")
            .ThenStatus(HttpStatusCode.Accepted)
            .ThenStatus(HttpStatusCode.NotFound);

        CliException error = await Assert.ThrowsAsync<CliException>(() =>
            Client().CallToolAsync(McpToolNames.ListConnections, [], TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Failure, error.ExitCode);
        Assert.Equal(4, _http.Requests.Count);
    }

    [Fact]
    public async Task A_401_refreshes_the_token_once_and_retries()
    {
        StoreSession("session-A");
        _http.ThenStatus(HttpStatusCode.Unauthorized).ThenJson(ToolResult(1, "[]"));

        await Client().CallToolAsync(McpToolNames.ListConnections, [], TestContext.Current.CancellationToken);

        Assert.Equal(1, _tokens.Refreshes);
        Assert.Equal("Bearer access-1", _http.Requests[0].Header("Authorization"));
        Assert.Equal("Bearer access-2", _http.Requests[1].Header("Authorization"));
    }

    [Fact]
    public async Task A_second_401_after_refreshing_is_exit_4()
    {
        StoreSession("session-A");
        _http.ThenStatus(HttpStatusCode.Unauthorized).ThenStatus(HttpStatusCode.Unauthorized);

        CliException error = await Assert.ThrowsAsync<CliException>(() =>
            Client().CallToolAsync(McpToolNames.ListConnections, [], TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.AuthenticationRequired, error.ExitCode);
        Assert.Equal(1, _tokens.Refreshes);
    }

    [Fact]
    public async Task An_event_stream_answer_is_read_until_the_message_with_the_request_id()
    {
        StoreSession("session-A");
        var stream =
            "event: message\n"
            + "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progress\":1}}\n\n"
            + "event: message\n"
            + "data: " + ToolResult(1, "{\"taskId\":\"t_1\"}") + "\n\n";
        _http.Then(_ => FakeHttpHandler.EventStream(stream));

        McpToolResult result = await Client().CallToolAsync(McpToolNames.GetStatus, new JsonObject { ["taskId"] = "t_1" }, TestContext.Current.CancellationToken);

        Assert.Equal("{\"taskId\":\"t_1\"}", result.Texts[0]);
    }

    [Fact]
    public async Task A_json_rpc_error_for_invalid_params_is_a_usage_error()
    {
        StoreSession("session-A");
        _http.ThenJson("""{"jsonrpc":"2.0","id":1,"error":{"code":-32602,"message":"Unknown tool: nope"}}""");

        CliException error = await Assert.ThrowsAsync<CliException>(() =>
            Client().CallToolAsync("nope", [], TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.Usage, error.ExitCode);
        Assert.Equal("Unknown tool: nope", error.Message);
    }

    public void Dispose() => _directory.Dispose();

    private const string InitializeResult =
        """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18","capabilities":{"tools":{}},"serverInfo":{"name":"velixo-mcp-server","version":"0.1.0"}}}""";

    private static string ToolResult(int id, string text) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                ["isError"] = false,
            },
        }.ToJsonString();

    private StreamableHttpMcpClient Client(string? agent = null) =>
        new(new HttpClient(_http), _endpoint, _tokens, _profiles, Profile, agent, "0.1.0-test");

    private void StoreSession(string sessionId) =>
        _profiles.UpdateState(Profile, state =>
        {
            state.McpSessionId = sessionId;
            state.McpUrl = _endpoint.ToString();
        });
}
