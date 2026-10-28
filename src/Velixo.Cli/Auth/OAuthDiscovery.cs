using System.Net;
using Velixo.Cli.Output;
using Velixo.Cli.Serialization;

namespace Velixo.Cli.Auth;

/// <summary>
/// Finds the authorization server of an MCP URL the way an MCP host does: the protected-resource
/// metadata on the URL's origin names it, and its own metadata lists the endpoints.
/// </summary>
public sealed class OAuthDiscovery(HttpClient http)
{
    public async Task<OAuthServerEndpoints> DiscoverAsync(Uri mcpUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mcpUrl);

        var origin = mcpUrl.GetLeftPart(UriPartial.Authority);
        ProtectedResourceMetadata? resource = await GetOptionalAsync(
            new Uri(origin + "/.well-known/oauth-protected-resource"),
            cancellationToken).ConfigureAwait(false);

        var issuer = (resource?.AuthorizationServers?.FirstOrDefault() ?? origin).TrimEnd('/');

        using var request = new HttpRequestMessage(HttpMethod.Get, issuer + "/.well-known/oauth-authorization-server");
        using HttpResponseMessage response = await HttpCalls.SendAsync(http, request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.Http,
                $"{issuer} did not publish its sign-in endpoints (HTTP {(int)response.StatusCode}).",
                "Check the --env or --url value.");
        }

        AuthorizationServerMetadata metadata = await HttpCalls
            .ReadJsonAsync(response, CliJsonContext.Default.AuthorizationServerMetadata, "The authorization server metadata", cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(metadata.AuthorizationEndpoint) || string.IsNullOrEmpty(metadata.TokenEndpoint))
        {
            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.Protocol,
                $"{issuer} published no authorization or token endpoint.");
        }

        return new OAuthServerEndpoints
        {
            Issuer = (metadata.Issuer ?? issuer).TrimEnd('/'),
            AuthorizationEndpoint = metadata.AuthorizationEndpoint,
            TokenEndpoint = metadata.TokenEndpoint,
            RegistrationEndpoint = metadata.RegistrationEndpoint,
            RevocationEndpoint = metadata.RevocationEndpoint,
            Resource = resource?.Resource ?? mcpUrl.ToString(),
        };
    }

    private async Task<ProtectedResourceMetadata?> GetOptionalAsync(Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = await HttpCalls.SendAsync(http, request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.Http,
                $"{url.GetLeftPart(UriPartial.Authority)} answered HTTP {(int)response.StatusCode} to the protected-resource metadata request.",
                "Check the --env or --url value, and retry.");
        }

        return await HttpCalls
            .ReadJsonAsync(response, CliJsonContext.Default.ProtectedResourceMetadata, "The protected-resource metadata", cancellationToken)
            .ConfigureAwait(false);
    }
}
