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
/// public client on a server that does not know it. Without a browser the sign-in is split in two: the
/// first call prints the URL and keeps the PKCE verifier in the profile state, and
/// <see cref="CompleteAsync"/> redeems the code from the URL the browser landed on.
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
    /// Starts a sign-in that a person finishes in a browser anywhere, and returns the
    /// <c>authorization_required</c> document with the URL and the command that completes it.
    /// </summary>
    public async Task<JsonObject> BeginDeferredAsync(ResolvedSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        OAuthServerEndpoints endpoints = await _discovery.DiscoverAsync(settings.McpUrl, cancellationToken).ConfigureAwait(false);
        var verifier = Pkce.CreateVerifier();
        var state = Pkce.CreateState();
        var redirectUri = new Uri($"http://127.0.0.1:{LoopbackListener.FreeLoopbackPort()}/callback");
        (var clientId, Uri browserUrl) = await ChooseClientAsync(settings, endpoints, redirectUri, verifier, state, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset expiresAt = clock.GetUtcNow().Add(PendingLifetime);

        profiles.UpdateState(settings.ProfileName, profileState => profileState.PendingLogin = new PendingLogin
        {
            Issuer = endpoints.Issuer,
            TokenEndpoint = endpoints.TokenEndpoint,
            RevocationEndpoint = endpoints.RevocationEndpoint,
            ClientId = clientId,
            RedirectUri = redirectUri.ToString(),
            CodeVerifier = verifier,
            State = state,
            Resource = endpoints.Resource,
            McpUrl = settings.McpUrl.ToString(),
            ExpiresAt = expiresAt,
        });

        return new JsonObject
        {
            ["status"] = "authorization_required",
            ["browserUrl"] = browserUrl.ToString(),
            ["expiresAt"] = expiresAt,
            ["profile"] = settings.ProfileName,
            ["environment"] = settings.Target,
            ["nextStep"] = $"velixo login --profile {settings.ProfileName} --complete <paste the URL the browser lands on>",
        };
    }

    /// <summary>
    /// Finishes a sign-in begun by <see cref="BeginDeferredAsync"/> from the URL the browser landed on,
    /// or from the bare code in it.
    /// </summary>
    public async Task<JsonObject> CompleteAsync(ResolvedSettings settings, string urlOrCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(urlOrCode);

        var pasted = urlOrCode.Trim();
        Uri? callback = Uri.TryCreate(pasted, UriKind.Absolute, out Uri? parsed) && parsed.Query.Length > 0 ? parsed : null;
        var callbackState = callback is null ? null : HttpUtility.ParseQueryString(callback.Query)["state"];

        (var profileName, PendingLogin pending) = FindPending(settings.ProfileName, callbackState);
        if (pending.ExpiresAt <= clock.GetUtcNow())
        {
            throw new CliException(
                ExitCodes.AuthenticationRequired,
                ErrorCodes.SignInFailed,
                "The sign-in started for this profile has expired.",
                $"velixo login --profile {profileName} --no-browser");
        }

        var code = callback is null ? pasted : ReadCode(callback, pending.State!);
        var endpoints = new OAuthServerEndpoints
        {
            Issuer = pending.Issuer!,
            AuthorizationEndpoint = string.Empty,
            TokenEndpoint = pending.TokenEndpoint!,
            RevocationEndpoint = pending.RevocationEndpoint,
            Resource = pending.Resource!,
        };

        TokenResponse answer = await _tokenClient
            .RedeemCodeAsync(endpoints.TokenEndpoint, pending.ClientId!, code, pending.RedirectUri!, pending.CodeVerifier!, pending.Resource, cancellationToken)
            .ConfigureAwait(false);

        ResolvedSettings target = settings with
        {
            ProfileName = profileName,
            McpUrl = new Uri(pending.McpUrl!),
            EnvironmentName = Environments.NameOf(new Uri(pending.McpUrl!)),
        };

        return await StoreAsync(target, endpoints, pending.ClientId!, answer, cancellationToken).ConfigureAwait(false);
    }

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

    private (string Profile, PendingLogin Pending) FindPending(string profile, string? callbackState)
    {
        PendingLogin? own = profiles.LoadState(profile).PendingLogin;
        if (own is not null && (callbackState is null || own.State == callbackState))
        {
            return (profile, own);
        }

        if (callbackState is not null && Directory.Exists(profiles.Paths.StateDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(profiles.Paths.StateDirectory, "*.json"))
            {
                var candidate = Path.GetFileNameWithoutExtension(file);
                if (ProfileNames.IsValid(candidate) && profiles.LoadState(candidate).PendingLogin is { } other && other.State == callbackState)
                {
                    return (candidate, other);
                }
            }
        }

        throw CliException.Usage(
            $"No sign-in was started for the profile '{profile}' that this answer belongs to.",
            $"velixo login --profile {profile} --no-browser");
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
