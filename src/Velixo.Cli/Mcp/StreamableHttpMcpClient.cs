using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Auth;
using Velixo.Cli.Config;
using Velixo.Cli.Output;

namespace Velixo.Cli.Mcp;

/// <summary>
/// MCP over Streamable HTTP, one POST per JSON-RPC request, answered as JSON or as a server-sent
/// event stream. The session id from <c>initialize</c> is stored in the profile state and sent by
/// every later process, because the server keys the Acumatica screen session on it.
/// </summary>
/// <remarks>
/// A stored session the server no longer knows answers 404: the client initializes again, stores
/// the new id and retries the request once. A 401 refreshes the access token once and retries.
/// </remarks>
public sealed class StreamableHttpMcpClient(
    HttpClient http,
    Uri endpoint,
    IAccessTokenProvider tokens,
    IProfileStore profiles,
    string profile,
    string? agent,
    string clientVersion) : IMcpClient
{
    private int _nextId;
    private bool _sessionLoaded;

    public string? SessionId { get; private set; }

    public McpServerInfo? ServerInfo { get; private set; }

    /// <summary>Gets a value indicating whether this process replaced a stored session that the server no longer knew.</summary>
    public bool SessionWasReplaced { get; private set; }

    public async Task<JsonArray> ListToolsAsync(CancellationToken cancellationToken)
    {
        var tools = new JsonArray();
        string? cursor = null;

        do
        {
            JsonObject? parameters = cursor is null ? null : new JsonObject { ["cursor"] = cursor };
            JsonNode? page = await SendRequestAsync("tools/list", parameters, cancellationToken).ConfigureAwait(false);

            foreach (JsonNode? tool in page?["tools"] as JsonArray ?? [])
            {
                tools.Add(tool?.DeepClone());
            }

            cursor = (string?)page?["nextCursor"];
        }
        while (!string.IsNullOrEmpty(cursor));

        return tools;
    }

    public async Task<McpToolResult> CallToolAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(arguments);

        var parameters = new JsonObject
        {
            ["name"] = name,
            ["arguments"] = arguments.DeepClone(),
            ["_meta"] = new JsonObject { ["agent"] = agent },
        };

        JsonNode? result = await SendRequestAsync("tools/call", parameters, cancellationToken).ConfigureAwait(false);
        return McpToolResult.From(result);
    }

    /// <summary>Sends one JSON-RPC request in the stored session, opening a session first when there is none.</summary>
    public async Task<JsonNode?> SendRequestAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        LoadStoredSession();
        var initializedHere = false;
        if (SessionId is null)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            initializedHere = true;
        }

        var id = Interlocked.Increment(ref _nextId);
        using (HttpResponseMessage response = await PostAsync(Request(id, method, parameters), withSession: true, cancellationToken).ConfigureAwait(false))
        {
            if (response.StatusCode != HttpStatusCode.NotFound || initializedHere)
            {
                return await ReadResultAsync(response, id, cancellationToken).ConfigureAwait(false);
            }
        }

        SessionWasReplaced = true;
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        id = Interlocked.Increment(ref _nextId);
        using HttpResponseMessage retried = await PostAsync(Request(id, method, parameters), withSession: true, cancellationToken).ConfigureAwait(false);
        return await ReadResultAsync(retried, id, cancellationToken).ConfigureAwait(false);
    }

    private static string Request(int id, string method, JsonObject? parameters)
    {
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };

        if (parameters is not null)
        {
            request["params"] = parameters.DeepClone();
        }

        return JsonText.Compact(request);
    }

    private static string Notification(string method) =>
        JsonText.Compact(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method });

    private static CliException HttpFailure(HttpResponseMessage response, string body)
    {
        var status = (int)response.StatusCode;
        var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body;

        return response.StatusCode switch
        {
            HttpStatusCode.Forbidden => new CliException(
                ExitCodes.AuthenticationRequired,
                ErrorCodes.AccessDenied,
                $"The Velixo server refused access (HTTP 403): {detail}",
                "velixo login"),
            HttpStatusCode.NotFound => new CliException(
                ExitCodes.Failure,
                ErrorCodes.Http,
                $"{response.RequestMessage?.RequestUri} is not a Velixo server address (HTTP 404).",
                "Check --env, --url or VELIXO_URL."),
            _ => new CliException(
                ExitCodes.Failure,
                ErrorCodes.Http,
                $"The Velixo server answered HTTP {status}: {detail}",
                status >= 500 ? "Retry once; if it fails again, report it with the time it happened." : null),
        };
    }

    private static CliException JsonRpcFailure(JsonNode error)
    {
        var code = error["code"] is JsonValue value && value.TryGetValue(out int number) ? number : 0;
        var message = (string?)error["message"] ?? "The server answered with an error.";

        // -32601 method not found and -32602 invalid params are mistakes in what was sent.
        return code is -32601 or -32602
            ? new CliException(ExitCodes.Usage, ErrorCodes.Usage, message, "The CLI and the server disagree on this tool's arguments. Run 'velixo version --check' and update; report it if it persists.")
            : new CliException(ExitCodes.Failure, ErrorCodes.Protocol, $"Server error {code}: {message}");
    }

    private void LoadStoredSession()
    {
        if (_sessionLoaded)
        {
            return;
        }

        _sessionLoaded = true;
        ProfileState state = profiles.LoadState(profile);
        if (string.Equals(state.McpUrl, endpoint.ToString(), StringComparison.Ordinal))
        {
            SessionId = state.McpSessionId;
            ServerInfo = state.Server;
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        SessionId = null;

        var id = Interlocked.Increment(ref _nextId);
        var parameters = new JsonObject
        {
            ["protocolVersion"] = McpProtocol.Version,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = McpProtocol.ClientName,
                ["version"] = clientVersion,
                ["title"] = McpProtocol.ClientTitle,
            },
        };

        JsonNode? result;
        string? sessionId;
        using (HttpResponseMessage response = await PostAsync(Request(id, "initialize", parameters), withSession: false, cancellationToken).ConfigureAwait(false))
        {
            sessionId = response.Headers.TryGetValues(McpProtocol.SessionIdHeader, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
            result = await ReadResultAsync(response, id, cancellationToken).ConfigureAwait(false);
        }

        SessionId = sessionId;
        ServerInfo = new McpServerInfo
        {
            Name = (string?)result?["serverInfo"]?["name"],
            Version = (string?)result?["serverInfo"]?["version"],
        };

        using (HttpResponseMessage initialized = await PostAsync(Notification("notifications/initialized"), withSession: true, cancellationToken).ConfigureAwait(false))
        {
            if (!initialized.IsSuccessStatusCode)
            {
                throw HttpFailure(initialized, await HttpCalls.ReadSnippetAsync(initialized, cancellationToken).ConfigureAwait(false));
            }
        }

        profiles.UpdateState(profile, state =>
        {
            state.McpSessionId = SessionId;
            state.McpUrl = endpoint.ToString();
            state.Server = ServerInfo;
        });
    }

    /// <summary>POSTs <paramref name="body"/>, refreshing the access token once if the server answers 401.</summary>
    private async Task<HttpResponseMessage> PostAsync(string body, bool withSession, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, McpProtocol.JsonMediaType),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(McpProtocol.JsonMediaType));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(McpProtocol.EventStreamMediaType));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add(McpProtocol.ProtocolVersionHeader, McpProtocol.Version);
            if (withSession && SessionId is not null)
            {
                request.Headers.Add(McpProtocol.SessionIdHeader, SessionId);
            }

            HttpResponseMessage response = await HttpCalls
                .SendAsync(http, request, cancellationToken, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return response;
            }

            response.Dispose();
            if (attempt > 0)
            {
                throw new CliException(
                    ExitCodes.AuthenticationRequired,
                    ErrorCodes.SessionEnded,
                    "The Velixo server refused the access token after a refresh.",
                    $"velixo login --profile {profile}");
            }

            token = await tokens.RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<JsonNode?> ReadResultAsync(HttpResponseMessage response, int id, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw HttpFailure(response, await HttpCalls.ReadSnippetAsync(response, cancellationToken).ConfigureAwait(false));
        }

        JsonNode message = await ReadMessageAsync(response, id, cancellationToken).ConfigureAwait(false)
            ?? throw new CliException(ExitCodes.Failure, ErrorCodes.Protocol, $"The server sent no answer to request {id}.");

        if (message["error"] is JsonNode error)
        {
            throw JsonRpcFailure(error);
        }

        return message["result"];
    }

    private async Task<JsonNode?> ReadMessageAsync(HttpResponseMessage response, int id, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        if (string.Equals(mediaType, McpProtocol.EventStreamMediaType, StringComparison.OrdinalIgnoreCase))
        {
            await foreach (JsonNode message in SseReader.ReadMessagesAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                if (IsAnswerTo(message, id))
                {
                    return message;
                }
            }

            return null;
        }

        JsonNode? body;
        try
        {
            body = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new CliException(ExitCodes.Failure, ErrorCodes.Protocol, $"The server's answer is not JSON: {exception.Message}");
        }

        return body switch
        {
            JsonArray batch => batch.FirstOrDefault(item => item is not null && IsAnswerTo(item, id)),
            _ => body,
        };
    }

    private static bool IsAnswerTo(JsonNode message, int id) =>
        message["id"] is JsonValue value
        && ((value.TryGetValue(out int number) && number == id)
            || (value.TryGetValue(out string? text) && text == id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
