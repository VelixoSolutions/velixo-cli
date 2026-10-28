using System.Security.Cryptography;
using System.Text;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Serialization;

namespace Velixo.Cli.Auth;

/// <summary>
/// Serves the profile's access token from <c>credentials.json</c> and refreshes it at the gateway.
/// A refresh token in <c>VELIXO_TOKEN</c> takes the stored one's place; the rotated tokens it buys are
/// stored under its hash, so the next command with the same variable continues from them instead of
/// replaying a token the gateway already rotated.
/// </summary>
public sealed class GatewayTokenProvider(
    ITokenStore store,
    OAuthTokenClient tokenClient,
    OAuthDiscovery discovery,
    ResolvedSettings settings,
    TimeProvider clock) : IAccessTokenProvider
{
    private static readonly TimeSpan _expiryMargin = TimeSpan.FromMinutes(1);

    private StoredCredentials? _credentials;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        StoredCredentials credentials = await LoadAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(credentials.AccessToken)
            && credentials.AccessTokenExpiresAt is { } expiresAt
            && expiresAt - _expiryMargin > clock.GetUtcNow())
        {
            return credentials.AccessToken;
        }

        return await RefreshAsync(credentials, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        StoredCredentials credentials = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return await RefreshAsync(credentials, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The refresh token <c>velixo auth token</c> prints, after any <c>VELIXO_TOKEN</c> has been applied.</summary>
    public async Task<string> GetRefreshTokenAsync(CancellationToken cancellationToken)
    {
        StoredCredentials credentials = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return credentials.RefreshToken ?? throw CliException.NotSignedIn(settings.ProfileName);
    }

    internal static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task<StoredCredentials> LoadAsync(CancellationToken cancellationToken)
    {
        if (_credentials is not null)
        {
            return _credentials;
        }

        StoredCredentials? stored = store.Get(settings.ProfileName);

        if (settings.EnvironmentToken is { } environmentToken)
        {
            var hash = HashToken(environmentToken);
            if (stored is not null && stored.EnvironmentTokenHash == hash && !string.IsNullOrEmpty(stored.RefreshToken))
            {
                return _credentials = stored;
            }

            StoredCredentials seeded = await SeedFromEnvironmentTokenAsync(stored, environmentToken, hash, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(seeded, cancellationToken).ConfigureAwait(false);
            return seeded;
        }

        if (stored is null || string.IsNullOrEmpty(stored.RefreshToken))
        {
            throw CliException.NotSignedIn(settings.ProfileName);
        }

        if (stored.SessionExpiresAt is { } sessionEnd && sessionEnd <= clock.GetUtcNow())
        {
            throw new CliException(
                ExitCodes.AuthenticationRequired,
                ErrorCodes.SessionEnded,
                $"The Velixo session of the profile '{settings.ProfileName}' ended on {sessionEnd:u}.",
                $"velixo login --profile {settings.ProfileName}");
        }

        return _credentials = stored;
    }

    private async Task<StoredCredentials> SeedFromEnvironmentTokenAsync(
        StoredCredentials? stored,
        string environmentToken,
        string hash,
        CancellationToken cancellationToken)
    {
        var sameServer = stored?.TokenEndpoint is not null && stored.McpUrl == settings.McpUrl.ToString();
        OAuthServerEndpoints? endpoints = sameServer ? null : await discovery.DiscoverAsync(settings.McpUrl, cancellationToken).ConfigureAwait(false);

        var issuer = endpoints?.Issuer ?? stored!.Issuer!;
        var clientId = settings.EnvironmentClientId
            ?? (sameServer ? stored!.ClientId : null)
            ?? store.GetClientId(issuer)
            ?? throw new CliException(
                ExitCodes.AuthenticationRequired,
                ErrorCodes.NotSignedIn,
                $"{EnvironmentVariables.Token} is set, but this machine does not know the OAuth client the token was issued to.",
                $"Set {EnvironmentVariables.ClientId} to the clientId in the credentials.json of the machine that ran 'velixo auth token'.");

        return _credentials = new StoredCredentials
        {
            Issuer = issuer,
            TokenEndpoint = endpoints?.TokenEndpoint ?? stored!.TokenEndpoint,
            RevocationEndpoint = endpoints?.RevocationEndpoint ?? stored!.RevocationEndpoint,
            ClientId = clientId,
            Resource = endpoints?.Resource ?? stored!.Resource,
            McpUrl = settings.McpUrl.ToString(),
            RefreshToken = environmentToken,
            SignedInAt = clock.GetUtcNow(),
            EnvironmentTokenHash = hash,
        };
    }

    private async Task<string> RefreshAsync(StoredCredentials credentials, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(credentials.TokenEndpoint) || string.IsNullOrEmpty(credentials.ClientId) || string.IsNullOrEmpty(credentials.RefreshToken))
        {
            throw CliException.NotSignedIn(settings.ProfileName);
        }

        TokenResponse tokens = await tokenClient
            .RefreshAsync(credentials.TokenEndpoint, credentials.ClientId, credentials.RefreshToken, credentials.Resource, cancellationToken)
            .ConfigureAwait(false);

        SignInRecords.ApplyTokens(credentials, tokens, clock.GetUtcNow());
        store.Save(settings.ProfileName, credentials);
        _credentials = credentials;

        return credentials.AccessToken!;
    }
}
