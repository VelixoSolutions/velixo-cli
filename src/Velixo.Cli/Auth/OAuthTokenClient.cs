using System.Net;
using System.Text.Json;
using Velixo.Cli.Output;
using Velixo.Cli.Serialization;

namespace Velixo.Cli.Auth;

/// <summary>Calls the token, device authorization and revocation endpoints: the authorization_code, refresh_token and device_code grants.</summary>
public sealed class OAuthTokenClient(HttpClient http)
{
    /// <summary>The RFC 8628 grant type that redeems a device code.</summary>
    public const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

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

    /// <summary>Starts an RFC 8628 device code sign-in, which the person finishes in a browser on any device.</summary>
    public async Task<DeviceAuthorizationResponse> RequestDeviceCodeAsync(
        string deviceAuthorizationEndpoint,
        string clientId,
        string scope,
        string? resource,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, deviceAuthorizationEndpoint)
        {
            Content = new FormUrlEncodedContent(
            [
                new("client_id", clientId),
                new("scope", scope),
                .. ResourceField(resource),
            ]),
        };
        using HttpResponseMessage response = await HttpCalls.SendAsync(http, request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            OAuthErrorResponse? error = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
            throw new CliException(
                response.StatusCode >= HttpStatusCode.InternalServerError ? ExitCodes.Failure : ExitCodes.AuthenticationRequired,
                ErrorCodes.SignInFailed,
                $"The sign-in could not be started: {Describe(response.StatusCode, error)}",
                "Retry in a few seconds.");
        }

        DeviceAuthorizationResponse answer = await HttpCalls
            .ReadJsonAsync(response, CliJsonContext.Default.DeviceAuthorizationResponse, "The device authorization answer", cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrEmpty(answer.DeviceCode) || string.IsNullOrEmpty(answer.UserCode) || string.IsNullOrEmpty(answer.VerificationUri)
            ? throw new CliException(ExitCodes.Failure, ErrorCodes.Protocol, "The device authorization answer lacks its device_code, user_code or verification_uri.")
            : answer;
    }

    /// <summary>
    /// Asks once whether the person finished the device code sign-in. Returns the tokens once they have, or the
    /// RFC 8628 §3.5 error code otherwise: <c>authorization_pending</c>, <c>slow_down</c>, <c>access_denied</c> or <c>expired_token</c>.
    /// </summary>
    public async Task<(TokenResponse? Tokens, string? Error, string? Description)> PollDeviceCodeAsync(
        string tokenEndpoint,
        string clientId,
        string deviceCode,
        string? resource,
        CancellationToken cancellationToken)
    {
        (TokenResponse? tokens, HttpStatusCode status, OAuthErrorResponse? error) = await SendAsync(
            tokenEndpoint,
            [
                new("grant_type", DeviceCodeGrantType),
                new("device_code", deviceCode),
                new("client_id", clientId),
                .. ResourceField(resource),
            ],
            cancellationToken).ConfigureAwait(false);

        return (tokens, tokens is null ? error?.Error ?? $"http_{(int)status}" : null, tokens is null ? Describe(status, error) : null);
    }

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
        (TokenResponse? tokens, HttpStatusCode status, OAuthErrorResponse? error) = await SendAsync(tokenEndpoint, fields, cancellationToken).ConfigureAwait(false);
        if (tokens is not null)
        {
            return tokens;
        }

        var description = Describe(status, error);
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

    /// <summary>Posts a token request, and returns the tokens, or the error the server answered with. A 5xx answer throws.</summary>
    private async Task<(TokenResponse? Tokens, HttpStatusCode Status, OAuthErrorResponse? Error)> SendAsync(
        string tokenEndpoint,
        KeyValuePair<string, string>[] fields,
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
                : (tokens, response.StatusCode, null);
        }

        OAuthErrorResponse? error = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode >= HttpStatusCode.InternalServerError)
        {
            throw new CliException(ExitCodes.Failure, ErrorCodes.Http, $"The token endpoint failed: {Describe(response.StatusCode, error)}", "Retry in a few seconds.");
        }

        return (null, response.StatusCode, error);
    }

    private static async Task<OAuthErrorResponse?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize(body, CliJsonContext.Default.OAuthErrorResponse);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Describe(HttpStatusCode status, OAuthErrorResponse? error) =>
        error?.ErrorDescription ?? error?.Error ?? $"HTTP {(int)status}";
}
