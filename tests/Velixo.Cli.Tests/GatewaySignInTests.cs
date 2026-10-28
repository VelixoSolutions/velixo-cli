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
    [Fact]
    public async Task A_deferred_sign_in_completes_from_the_pasted_url()
    {
        using var test = new TestServices();
        test.Http
            .ThenJson("""{"resource":"https://nx-dev.velixo.com/mcp","authorization_servers":["https://nx-dev.velixo.com"]}""")
            .ThenJson(AuthorizationServer)
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

        JsonNode registration = JsonNode.Parse(test.Http.Requests[2].Body!)!;
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

        var exchange = HttpUtility.ParseQueryString(test.Http.Requests[3].Body!);
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
