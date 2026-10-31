using System.Net.Http.Json;
using Velixo.Cli.Output;
using Velixo.Cli.Serialization;

namespace Velixo.Cli.Auth;

/// <summary>
/// Registers the CLI as a public OAuth client (RFC 7591). The gateway matches a registered HTTP
/// loopback redirect against any port (RFC 8252 §7.3), so one portless registration serves every
/// <c>velixo login</c>.
/// </summary>
public sealed class DynamicClientRegistration(HttpClient http)
{
    public const string ClientName = "Velixo CLI";

    public const string Scope = "Mcp.Access";

    public static readonly Uri RegisteredRedirectUri = new("http://127.0.0.1/callback");

    public async Task<string> RegisterAsync(string registrationEndpoint, CancellationToken cancellationToken)
    {
        var registration = new ClientRegistrationRequest
        {
            ClientName = ClientName,
            RedirectUris = [RegisteredRedirectUri.ToString()],
            GrantTypes = ["authorization_code", "refresh_token"],
            ResponseTypes = ["code"],
            TokenEndpointAuthMethod = "none",
            Scope = Scope,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, registrationEndpoint)
        {
            Content = JsonContent.Create(registration, CliJsonContext.Default.ClientRegistrationRequest),
        };
        using HttpResponseMessage response = await HttpCalls.SendAsync(http, request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await HttpCalls.ReadSnippetAsync(response, cancellationToken).ConfigureAwait(false);
            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.SignInFailed,
                $"The CLI could not register with {registrationEndpoint} (HTTP {(int)response.StatusCode}): {body}");
        }

        ClientRegistrationResponse registered = await HttpCalls
            .ReadJsonAsync(response, CliJsonContext.Default.ClientRegistrationResponse, "The registration answer", cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrEmpty(registered.ClientId)
            ? throw new CliException(ExitCodes.Failure, ErrorCodes.Protocol, "The registration answer carries no client_id.")
            : registered.ClientId;
    }
}
