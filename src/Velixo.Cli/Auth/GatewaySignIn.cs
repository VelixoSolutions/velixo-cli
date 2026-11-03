using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Serialization;

namespace Velixo.Cli.Auth;

/// <summary>
/// Signs a profile in through the Velixo OAuth gateway: authorization code with PKCE, a loopback
/// redirect, and the first-party client <see cref="FirstPartyClientId"/>, or a dynamically registered
/// public client on a server that does not know it. Without a browser on this machine it uses the RFC 8628
/// device code grant instead: the person enters a short code in a browser anywhere, and the CLI polls the
/// token endpoint until they have signed in.
/// </summary>
/// <param name="httpWithoutRedirects">A client that hands back a redirect instead of following it.</param>
public sealed class GatewaySignIn(
    HttpClient http,
    HttpClient httpWithoutRedirects,
    ITokenStore tokens,
    IProfileStore profiles,
    IBrowserOpener browser,
    TimeProvider clock,
    OutputWriter output)
{
    /// <summary>How long the gateway holds a started authorization at its sign-in page.</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The RFC 8628 poll interval, in seconds, when the server names none, and what each slow_down adds to it.</summary>
    private const long DefaultPollSeconds = 5;

    /// <summary>The client id the gateway registers for the CLI itself, with the loopback redirect on any port.</summary>
    public const string FirstPartyClientId = "velixo-cli";

    private readonly OAuthDiscovery _discovery = new(http);
    private readonly OAuthTokenClient _tokenClient = new(http);

    /// <summary>Signs in with a browser on this machine and returns the signed-in document.</summary>
    public async Task<JsonObject> SignInWithBrowserAsync(ResolvedSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        OAuthServerEndpoints endpoints = await _discovery.DiscoverAsync(settings.McpUrl, cancellationToken).ConfigureAwait(false);
        var verifier = Pkce.CreateVerifier();
        var state = Pkce.CreateState();

        using LoopbackListener listener = LoopbackListener.Start();
        (var clientId, Uri authorizeUrl) = await ChooseClientAsync(settings, endpoints, listener.RedirectUri, verifier, state, cancellationToken)
            .ConfigureAwait(false);

        output.Progress($"Opening the browser to sign in to {settings.Target}.");
        output.Progress($"If it does not open, visit: {authorizeUrl}");
        _ = browser.TryOpen(authorizeUrl);

        using var deadline = new CancellationTokenSource(PendingLifetime, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        Uri callback;
        try
        {
            callback = await listener.WaitForCallbackAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new CliException(
                ExitCodes.AuthenticationRequired,
                ErrorCodes.SignInFailed,
                $"No sign-in arrived within {PendingLifetime.TotalMinutes:0} minutes.",
                "velixo login");
        }

        var code = ReadCode(callback, state);
        TokenResponse answer = await _tokenClient
            .RedeemCodeAsync(endpoints.TokenEndpoint, clientId, code, listener.RedirectUri.ToString(), verifier, endpoints.Resource, cancellationToken)
            .ConfigureAwait(false);

        return await StoreAsync(settings, endpoints, clientId, answer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts an RFC 8628 device code sign-in, which a person finishes by entering a short code in a browser on
    /// any device. Keeps the device code in the profile state, so <see cref="WaitForDeviceSignInAsync"/> in this
    /// process or a later one can finish it, and returns the <c>authorization_required</c> document.
    /// </summary>
    public async Task<JsonObject> BeginDeviceSignInAsync(ResolvedSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        OAuthServerEndpoints endpoints = await _discovery.DiscoverAsync(settings.McpUrl, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(endpoints.DeviceAuthorizationEndpoint))
        {
            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.SignInFailed,
                $"{endpoints.Issuer} offers no sign-in without a browser on this machine (no device_authorization_endpoint).",
                "Run 'velixo login' on a machine with a browser, then pass its 'velixo auth token' as VELIXO_TOKEN here.");
        }

        var clientId = settings.EnvironmentClientId ?? FirstPartyClientId;
        DeviceAuthorizationResponse device = await _tokenClient
            .RequestDeviceCodeAsync(endpoints.DeviceAuthorizationEndpoint, clientId, DynamicClientRegistration.Scope, endpoints.Resource, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset expiresAt = clock.GetUtcNow().Add(device.ExpiresIn is > 0 and var seconds ? TimeSpan.FromSeconds(seconds) : PendingLifetime);
        var pending = new PendingLogin
        {
            Issuer = endpoints.Issuer,
            TokenEndpoint = endpoints.TokenEndpoint,
            RevocationEndpoint = endpoints.RevocationEndpoint,
            ClientId = clientId,
            DeviceCode = device.DeviceCode,
            UserCode = device.UserCode,
            VerificationUri = device.VerificationUri,
            VerificationUriComplete = device.VerificationUriComplete,
            IntervalSeconds = device.Interval is >= 0 and var interval ? interval : DefaultPollSeconds,
            Resource = endpoints.Resource,
            McpUrl = settings.McpUrl.ToString(),
            ExpiresAt = expiresAt,
        };
        profiles.UpdateState(settings.ProfileName, profileState => profileState.PendingLogin = pending);

        return new JsonObject
        {
            ["status"] = "authorization_required",
            ["verificationUri"] = pending.VerificationUri,
            ["verificationUriComplete"] = pending.VerificationUriComplete,
            ["userCode"] = pending.UserCode,
            ["expiresAt"] = expiresAt,
            ["profile"] = settings.ProfileName,
            ["environment"] = settings.Target,
            ["nextStep"] = $"velixo login --profile {settings.ProfileName} --complete",
        };
    }

    /// <summary>
    /// Waits for the person to finish the device code sign-in started by <see cref="BeginDeviceSignInAsync"/>,
    /// polling at the interval the server asks for, and returns the signed-in document.
    /// </summary>
    public async Task<JsonObject> WaitForDeviceSignInAsync(ResolvedSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        PendingLogin pending = profiles.LoadState(settings.ProfileName).PendingLogin is { DeviceCode.Length: > 0 } started
            ? started
            : throw CliException.Usage(
                $"No sign-in without a browser was started for the profile '{settings.ProfileName}'.",
                $"velixo login --profile {settings.ProfileName} --no-browser");

        TimeSpan interval = TimeSpan.FromSeconds(pending.IntervalSeconds);
        while (true)
        {
            if (pending.ExpiresAt <= clock.GetUtcNow())
            {
                throw Expired(settings.ProfileName);
            }

            (TokenResponse? tokens, var error, var description) = await _tokenClient
                .PollDeviceCodeAsync(pending.TokenEndpoint!, pending.ClientId!, pending.DeviceCode!, pending.Resource, cancellationToken)
                .ConfigureAwait(false);

            if (tokens is not null)
            {
                var endpoints = new OAuthServerEndpoints
                {
                    Issuer = pending.Issuer!,
                    AuthorizationEndpoint = string.Empty,
                    TokenEndpoint = pending.TokenEndpoint!,
                    RevocationEndpoint = pending.RevocationEndpoint,
                    Resource = pending.Resource!,
                };
                ResolvedSettings target = settings with
                {
                    McpUrl = new Uri(pending.McpUrl!),
                    EnvironmentName = Environments.NameOf(new Uri(pending.McpUrl!)),
                };

                return await StoreAsync(target, endpoints, pending.ClientId!, tokens, cancellationToken).ConfigureAwait(false);
            }

            switch (error)
            {
                case "authorization_pending":
                    break;
                case "slow_down":
                    // RFC 8628 §3.5: every slow_down adds five seconds to the interval, for the rest of this sign-in.
                    interval += TimeSpan.FromSeconds(DefaultPollSeconds);
                    break;
                case "access_denied":
                    ClearPending(settings.ProfileName);
                    throw new CliException(ExitCodes.AuthenticationRequired, ErrorCodes.SignInFailed, "The sign-in was declined in the browser.", "velixo login");
                case "expired_token":
                    throw Expired(settings.ProfileName);
                default:
                    ClearPending(settings.ProfileName);
                    throw new CliException(
                        ExitCodes.AuthenticationRequired,
                        ErrorCodes.SignInFailed,
                        $"The sign-in could not be completed: {description}",
                        $"velixo login --profile {settings.ProfileName} --no-browser");
            }

            await Task.Delay(interval, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private CliException Expired(string profile)
    {
        ClearPending(profile);
        return new CliException(
            ExitCodes.AuthenticationRequired,
            ErrorCodes.SignInFailed,
            "The code expired before the sign-in was finished in the browser.",
            $"velixo login --profile {profile} --no-browser");
    }

    private void ClearPending(string profile) => profiles.UpdateState(profile, state => state.PendingLogin = null);

    private static Uri BuildAuthorizeUrl(OAuthServerEndpoints endpoints, string clientId, Uri redirectUri, string verifier, string state)
    {
        var query = new StringBuilder();
        void Add(string name, string value) =>
            query.Append(query.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));

        Add("response_type", "code");
        Add("client_id", clientId);
        Add("redirect_uri", redirectUri.ToString());
        Add("code_challenge", Pkce.ComputeChallenge(verifier));
        Add("code_challenge_method", Pkce.S256Method);
        Add("state", state);
        Add("scope", DynamicClientRegistration.Scope);
        Add("resource", endpoints.Resource);

        return new Uri(endpoints.AuthorizationEndpoint + query);
    }

    private static string ReadCode(Uri callback, string expectedState)
    {
        var query = HttpUtility.ParseQueryString(callback.Query);

        if (query["error"] is { } error)
        {
            throw new CliException(
                ExitCodes.AuthenticationRequired,
                ErrorCodes.SignInFailed,
                $"The sign-in was refused: {query["error_description"] ?? error}",
                "velixo login");
        }

        if (!string.Equals(query["state"], expectedState, StringComparison.Ordinal))
        {
            throw new CliException(
                ExitCodes.AuthenticationRequired,
                ErrorCodes.SignInFailed,
                "The sign-in answer does not belong to the sign-in this CLI started.",
                "velixo login");
        }

        return query["code"] is { Length: > 0 } code
            ? code
            : throw new CliException(ExitCodes.AuthenticationRequired, ErrorCodes.SignInFailed, "The sign-in answer carries no code.", "velixo login");
    }

    /// <summary>
    /// Chooses the client id for this sign-in and the URL the browser opens. <c>VELIXO_CLIENT_ID</c> wins.
    /// Otherwise the authorize URL is sent once with <see cref="FirstPartyClientId"/>, even when a
    /// registered client is stored for the issuer, so a machine that signed in before the gateway knew
    /// the CLI moves to it: the sign-in page shows a first-party client as verified and a registration
    /// with the warning that Velixo has not verified it. A redirect means the server knows the client,
    /// and the browser opens the redirect's target, so the user completes the authorization this request
    /// started. Any other answer reuses the stored registration, or registers one.
    /// </summary>
    private async Task<(string ClientId, Uri BrowserUrl)> ChooseClientAsync(
        ResolvedSettings settings,
        OAuthServerEndpoints endpoints,
        Uri redirectUri,
        string verifier,
        string state,
        CancellationToken cancellationToken)
    {
        Uri AuthorizeUrl(string clientId) => BuildAuthorizeUrl(endpoints, clientId, redirectUri, verifier, state);

        if (settings.EnvironmentClientId is { } configured)
        {
            return (configured, AuthorizeUrl(configured));
        }

        if (await FindSignInPageAsync(AuthorizeUrl(FirstPartyClientId), cancellationToken).ConfigureAwait(false) is { } signInPage)
        {
            tokens.SaveClientId(endpoints.Issuer, FirstPartyClientId, clock.GetUtcNow());
            return (FirstPartyClientId, signInPage);
        }

        if (tokens.GetClientId(endpoints.Issuer) is { Length: > 0 } stored && stored != FirstPartyClientId)
        {
            return (stored, AuthorizeUrl(stored));
        }

        var registered = await RegisterAsync(endpoints, cancellationToken).ConfigureAwait(false);
        return (registered, AuthorizeUrl(registered));
    }

    /// <summary>Returns where the server redirects <paramref name="authorizeUrl"/> to, or null when it answers without a redirect.</summary>
    private async Task<Uri?> FindSignInPageAsync(Uri authorizeUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, authorizeUrl);
        using HttpResponseMessage response = await HttpCalls
            .SendAsync(httpWithoutRedirects, request, cancellationToken, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);

        var status = (int)response.StatusCode;
        return status is >= 300 and < 400 && response.Headers.Location is { } location
            ? new Uri(authorizeUrl, location)
            : null;
    }

    private async Task<string> RegisterAsync(OAuthServerEndpoints endpoints, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(endpoints.RegistrationEndpoint))
        {
            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.SignInFailed,
                $"{endpoints.Issuer} offers no client registration, and the CLI has no client id for it.");
        }

        var clientId = await new DynamicClientRegistration(http)
            .RegisterAsync(endpoints.RegistrationEndpoint, cancellationToken)
            .ConfigureAwait(false);
        tokens.SaveClientId(endpoints.Issuer, clientId, clock.GetUtcNow());
        return clientId;
    }

    private async Task<JsonObject> StoreAsync(
        ResolvedSettings settings,
        OAuthServerEndpoints endpoints,
        string clientId,
        TokenResponse answer,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        var credentials = new StoredCredentials
        {
            Issuer = endpoints.Issuer,
            TokenEndpoint = endpoints.TokenEndpoint,
            RevocationEndpoint = endpoints.RevocationEndpoint,
            ClientId = clientId,
            Resource = endpoints.Resource,
            McpUrl = settings.McpUrl.ToString(),
            SignedInAt = now,
        };
        SignInRecords.ApplyTokens(credentials, answer, now);

        // A refresh running in another process would otherwise write the previous session back over this one.
        // The MCP session belonged to whoever was signed in before, and the server refuses to hand it to anyone
        // else, so it is dropped under the same lock: no command can pair it with the new tokens in between.
        using (await tokens.LockForRefreshAsync(settings.ProfileName, cancellationToken).ConfigureAwait(false))
        {
            tokens.Save(settings.ProfileName, credentials);
            profiles.UpdateState(settings.ProfileName, state =>
            {
                state.PendingLogin = null;
                state.McpSessionId = null;
                state.LastContinuation = null;
            });
        }

        profiles.UpdateConfig(config =>
        {
            ProfileConfig profile = config.Profiles.GetValueOrDefault(settings.ProfileName) ?? new ProfileConfig();
            profile.Environment = settings.EnvironmentName;
            profile.Url = settings.EnvironmentName is null ? settings.McpUrl.ToString() : null;
            config.Profiles[settings.ProfileName] = profile;
            config.DefaultProfile ??= settings.ProfileName;
        });


        return SignInRecords.Describe(credentials, settings.ProfileName, settings.Target);
    }
}
