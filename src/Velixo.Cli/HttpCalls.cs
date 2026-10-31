using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Velixo.Cli.Output;

namespace Velixo.Cli;

/// <summary>
/// Sends an HTTP request and turns a transport failure into the CLI's network error (exit 1), so no
/// command has to know how <see cref="HttpClient"/> reports a refused connection or a timeout.
/// </summary>
public static class HttpCalls
{
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await http.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.Network,
                $"Could not reach {request.RequestUri?.GetLeftPart(UriPartial.Authority)}: {exception.Message}",
                "Check the network and the proxy settings, then retry.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.Network,
                $"{request.RequestUri?.GetLeftPart(UriPartial.Authority)} did not answer in time.",
                "Retry; if it keeps happening, check the server's status.",
                exception);
        }
    }

    public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, string what, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize(body, typeInfo)
                ?? throw new JsonException("The document is empty.");
        }
        catch (JsonException exception)
        {
            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.Protocol,
                $"{what} from {response.RequestMessage?.RequestUri} is not valid JSON: {exception.Message}",
                nextStep: null,
                exception);
        }
    }

    /// <summary>The first 300 characters of a response body, for an error message.</summary>
    public static async Task<string> ReadSnippetAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var body = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
        return body.Length > 300 ? string.Concat(body.AsSpan(0, 300), "…") : body;
    }
}
