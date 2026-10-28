using System.Net;
using System.Text;

namespace Velixo.Cli.Tests.Fakes;

/// <summary>Answers each request with the next scripted response, and records what was sent.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public FakeHttpHandler Then(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _responses.Enqueue(respond);
        return this;
    }

    public FakeHttpHandler ThenJson(string json, HttpStatusCode status = HttpStatusCode.OK, string? sessionId = null) =>
        Then(_ => Json(json, status, sessionId));

    public FakeHttpHandler ThenStatus(HttpStatusCode status) => Then(_ => new HttpResponseMessage(status));

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, string? sessionId = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (sessionId is not null)
        {
            response.Headers.Add("Mcp-Session-Id", sessionId);
        }

        return response;
    }

    public static HttpResponseMessage EventStream(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase),
            body));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No scripted response for {request.Method} {request.RequestUri}.");
        }

        HttpResponseMessage response = _responses.Dequeue()(request);
        response.RequestMessage = request;
        return response;
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? Body)
{
    public string? Header(string name) => Headers.GetValueOrDefault(name);
}
