using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Velixo.Cli.Auth;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class GatewaySignInTests
{
    private const string Issuer = "https://nx-dev.velixo.com";

    private const string ProtectedResource =
        """{"resource":"https://nx-dev.velixo.com/mcp","authorization_servers":["https://nx-dev.velixo.com"]}""";

    [Fact]
    public async Task A_deferred_sign_in_completes_from_the_pasted_url()
    {
        using var test = new TestServices();
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson("""{"error":"invalid_client"}""", HttpStatusCode.BadRequest)
            .ThenJson("""{"client_id":"dcr_test"}""", HttpStatusCode.Created);
        await test.RunAsync("login", "--env", "nx-dev", "--no-browser");

        JsonNode begun = JsonNode.Parse(test.Console.StdOut)!;
        Assert.Equal("authorization_required", (string?)begun["status"]);
        Assert.Equal("velixo login --profile nx-dev --complete <paste the URL the browser lands on>", (string?)begun["nextStep"]);

        var authorize = new Uri((string)begun["browserUrl"]!);
        var query = HttpUtility.ParseQueryString(authorize.Query);
        Assert.Equal("https://nx-dev.velixo.com/oauth/authorize", authorize.GetLeftPart(UriPartial.Path));
        Assert.Equal("dcr_test", query["client_id"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(43, query["code_challenge"]!.Length);
        Assert.Equal("Mcp.Access", query["scope"]);
        Assert.Equal("https://nx-dev.velixo.com/mcp", query["resource"]);
        Assert.StartsWith("http://127.0.0.1:", query["redirect_uri"], StringComparison.Ordinal);

        Assert.Equal(GatewaySignIn.FirstPartyClientId, HttpUtility.ParseQueryString(test.Http.Requests[2].Uri.Query)["client_id"]);
        Assert.Equal(query["state"], HttpUtility.ParseQueryString(test.Http.Requests[2].Uri.Query)["state"]);
        Assert.Equal("dcr_test", test.Services.Tokens.GetClientId(Issuer));

        JsonNode registration = JsonNode.Parse(test.Http.Requests[3].Body!)!;
        Assert.Equal("Velixo CLI", (string?)registration["client_name"]);
        Assert.Equal("http://127.0.0.1/callback", (string?)registration["redirect_uris"]![0]);
        Assert.Equal("none", (string?)registration["token_endpoint_auth_method"]);

        test.Http.ThenJson(TokenAnswer("dev@example.com", "org-42"));
        test.Console.OutWriter.GetStringBuilder().Clear();
        var landed = $"{query["redirect_uri"]}?code=the-code&state={Uri.EscapeDataString(query["state"]!)}";

        var exit = await test.RunAsync("login", "--complete", landed);

        Assert.Equal(ExitCodes.Success, exit);
        JsonNode signedIn = JsonNode.Parse(test.Console.StdOut)!;
        Assert.Equal("dev@example.com", (string?)signedIn["user"]!["email"]);
        Assert.Equal("org-42", (string?)signedIn["organization"]!["id"]);
        Assert.Equal("nx-dev", (string?)signedIn["profile"]);

        var exchange = HttpUtility.ParseQueryString(test.Http.Requests[4].Body!);
        Assert.Equal("authorization_code", exchange["grant_type"]);
        Assert.Equal("the-code", exchange["code"]);
        Assert.Equal(query["redirect_uri"], exchange["redirect_uri"]);
        Assert.Equal(query["code_challenge"], Pkce.ComputeChallenge(exchange["code_verifier"]!));

        Assert.Equal("refresh-1", test.Services.Tokens.Get("nx-dev")!.RefreshToken);
        Assert.Equal("nx-dev", test.Services.Profiles.LoadConfig().DefaultProfile);
        Assert.Null(test.Services.Profiles.LoadState("nx-dev").PendingLogin);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(test.Paths.CredentialsFile));
        }
    }

    [Fact]
    public async Task A_server_that_knows_the_first_party_client_gets_it_and_the_browser_opens_its_redirect()
    {
        using var test = new TestServices();
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .Then(_ => Redirect("/signin?request=pending-1"));

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--no-browser");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("https://nx-dev.velixo.com/signin?request=pending-1", (string?)JsonNode.Parse(test.Console.StdOut)!["browserUrl"]);
        Assert.Equal(3, test.Http.Requests.Count);
        var probe = HttpUtility.ParseQueryString(test.Http.Requests[2].Uri.Query);
        Assert.Equal("https://nx-dev.velixo.com/oauth/authorize", test.Http.Requests[2].Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal(GatewaySignIn.FirstPartyClientId, probe["client_id"]);

        PendingLogin pending = test.Services.Profiles.LoadState("nx-dev").PendingLogin!;
        Assert.Equal(GatewaySignIn.FirstPartyClientId, pending.ClientId);
        Assert.Equal(pending.State, probe["state"]);
        Assert.Equal(pending.RedirectUri, probe["redirect_uri"]);
        Assert.Equal(GatewaySignIn.FirstPartyClientId, test.Services.Tokens.GetClientId(Issuer));
    }

    [Fact]
    public async Task A_stored_registered_client_gives_way_to_the_first_party_client_where_the_server_knows_it()
    {
        using var test = new TestServices();
        test.Services.Tokens.SaveClientId(Issuer, "dcr_stored", DateTimeOffset.UtcNow);
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .Then(_ => Redirect("/signin?request=pending-2"));

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--no-browser");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(3, test.Http.Requests.Count);
        Assert.Equal(GatewaySignIn.FirstPartyClientId, test.Services.Profiles.LoadState("nx-dev").PendingLogin!.ClientId);
        Assert.Equal(GatewaySignIn.FirstPartyClientId, test.Services.Tokens.GetClientId(Issuer));
    }

    [Fact]
    public async Task A_stored_registered_client_is_reused_where_the_server_does_not_know_the_first_party_client()
    {
        using var test = new TestServices();
        test.Services.Tokens.SaveClientId(Issuer, "dcr_stored", DateTimeOffset.UtcNow);
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson("""{"error":"invalid_client"}""", HttpStatusCode.BadRequest);

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--no-browser");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(3, test.Http.Requests.Count);
        Assert.Equal("dcr_stored", BrowserClientId(test));
        Assert.Equal("dcr_stored", test.Services.Tokens.GetClientId(Issuer));
    }

    [Fact]
    public async Task A_stored_first_party_client_is_probed_again_and_falls_back_to_a_registration()
    {
        using var test = new TestServices();
        test.Services.Tokens.SaveClientId(Issuer, GatewaySignIn.FirstPartyClientId, DateTimeOffset.UtcNow);
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson("""{"error":"invalid_client"}""", HttpStatusCode.BadRequest)
            .ThenJson("""{"client_id":"dcr_new"}""", HttpStatusCode.Created);

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--no-browser");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("dcr_new", BrowserClientId(test));
        Assert.Equal("dcr_new", test.Services.Tokens.GetClientId(Issuer));
    }

    [Fact]
    public async Task The_client_id_variable_skips_the_probe()
    {
        using var test = new TestServices(new Dictionary<string, string> { ["VELIXO_CLIENT_ID"] = "configured-client" });
        test.Http.ThenJson(ProtectedResource).ThenJson(AuthorizationServer);

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--no-browser");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(2, test.Http.Requests.Count);
        Assert.Equal("configured-client", BrowserClientId(test));
    }

    [Fact]
    public async Task A_pasted_url_with_another_state_is_refused()
    {
        using var test = new TestServices();
        test.Services.Profiles.SaveState("nx-dev", new ProfileState
        {
            PendingLogin = new PendingLogin { State = "expected", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) },
        });

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--complete", "http://127.0.0.1:5000/callback?code=c&state=forged");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(test.Http.Requests);
    }

    [Fact]
    public async Task The_loopback_listener_hands_back_the_callback_url()
    {
        using LoopbackListener listener = LoopbackListener.Start();
        Task<Uri> waiting = listener.WaitForCallbackAsync(TestContext.Current.CancellationToken);

        using var http = new HttpClient();
        using HttpResponseMessage response = await http.GetAsync(new Uri(listener.RedirectUri + "?code=abc&state=xyz"), TestContext.Current.CancellationToken);
        Uri callback = await waiting;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("abc", HttpUtility.ParseQueryString(callback.Query)["code"]);
        Assert.Equal(listener.RedirectUri.Port, callback.Port);
    }

    internal const string AuthorizationServer =
        """
        {"issuer":"https://nx-dev.velixo.com","authorization_endpoint":"https://nx-dev.velixo.com/oauth/authorize",
         "token_endpoint":"https://nx-dev.velixo.com/oauth/token","registration_endpoint":"https://nx-dev.velixo.com/oauth/register",
         "revocation_endpoint":"https://nx-dev.velixo.com/oauth/revoke"}
        """;

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.Relative);
        return response;
    }

    private static string? BrowserClientId(TestServices test)
    {
        var browserUrl = new Uri((string)JsonNode.Parse(test.Console.StdOut)!["browserUrl"]!);
        return HttpUtility.ParseQueryString(browserUrl.Query)["client_id"];
    }

    internal static string TokenAnswer(string email, string organizationId, string refreshToken = "refresh-1")
    {
        var payload = new JsonObject
        {
            ["sub"] = "user-1",
            ["preferred_username"] = email,
            ["organizationId"] = organizationId,
            ["scp"] = "Mcp.Access",
        };
        var jwt = $"e30.{Base64Url.Encode(Encoding.UTF8.GetBytes(payload.ToJsonString()))}.sig";

        return new JsonObject
        {
            ["access_token"] = jwt,
            ["token_type"] = "Bearer",
            ["expires_in"] = 3600,
            ["refresh_token"] = refreshToken,
            ["scope"] = "Mcp.Access",
        }.ToJsonString();
    }
}
