using System.Net;
using System.Text.Json;
using Velixo.Cli.Output;
using Velixo.Cli.Serialization;

namespace Velixo.Cli.Auth;

/// <summary>Calls the token and revocation endpoints. Only the authorization_code and refresh_token grants exist on the gateway.</summary>
public sealed class OAuthTokenClient(HttpClient http)
{
    public Task<TokenResponse> RedeemCodeAsync(
        string tokenEndpoint,
        string clientId,
        string code,
        string redirectUri,
        string codeVerifier,
        string? resource,
        CancellationToken cancellationToken) =>
        PostAsync(
            tokenEndpoint,
            [
                new("grant_type", "authorization_code"),
                new("code", code),
                new("redirect_uri", redirectUri),
                new("client_id", clientId),
                new("code_verifier", codeVerifier),
                .. ResourceField(resource),
            ],
            isRefresh: false,
            cancellationToken);

    public Task<TokenResponse> RefreshAsync(
        string tokenEndpoint,
        string clientId,
        string refreshToken,
        string? resource,
        CancellationToken cancellationToken) =>
        PostAsync(
            tokenEndpoint,
            [
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken),
                new("client_id", clientId),
                .. ResourceField(resource),
            ],
            isRefresh: true,
            cancellationToken);

    /// <summary>Revokes <paramref name="token"/>. Best effort: RFC 7009 answers 200 whether or not the token was live.</summary>
    public async Task RevokeAsync(string revocationEndpoint, string clientId, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, revocationEndpoint)
        {
            Content = new FormUrlEncodedContent(
            [
                new("token", token),
                new("token_type_hint", "refresh_token"),
                new("client_id", clientId),
            ]),
        };
        using HttpResponseMessage response = await HttpCalls.SendAsync(http, request, cancellationToken).ConfigureAwait(false);
    }

    private static KeyValuePair<string, string>[] ResourceField(string? resource) =>
        string.IsNullOrEmpty(resource) ? [] : [new("resource", resource)];

    private async Task<TokenResponse> PostAsync(
        string tokenEndpoint,
        KeyValuePair<string, string>[] fields,
        bool isRefresh,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint) { Content = new FormUrlEncodedContent(fields) };
        using HttpResponseMessage response = await HttpCalls.SendAsync(http, request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            TokenResponse tokens = await HttpCalls
                .ReadJsonAsync(response, CliJsonContext.Default.TokenResponse, "The token answer", cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrEmpty(tokens.AccessToken)
                ? throw new CliException(ExitCodes.Failure, ErrorCodes.Protocol, "The token answer carries no access_token.")
                : tokens;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        OAuthErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize(body, CliJsonContext.Default.OAuthErrorResponse);
        }
        catch (JsonException)
        {
        }

        var description = error?.ErrorDescription ?? error?.Error ?? $"HTTP {(int)response.StatusCode}";

        if (response.StatusCode >= HttpStatusCode.InternalServerError)
        {
            throw new CliException(ExitCodes.Failure, ErrorCodes.Http, $"The token endpoint failed: {description}", "Retry in a few seconds.");
        }

        throw isRefresh
            ? new CliException(
                ExitCodes.AuthenticationRequired,
                ErrorCodes.SessionEnded,
                $"Your Velixo session has ended: {description}",
                "velixo login")
            : new CliException(
                ExitCodes.AuthenticationRequired,
                ErrorCodes.SignInFailed,
                $"The sign-in could not be completed: {description}",
                "Run 'velixo login' again; an authorization code is good for one minute and one use.");
    }
}
