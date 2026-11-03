using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Velixo.Cli.Auth;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Serialization;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class GatewaySignInTests
{
    private const string Issuer = "https://nx-dev.velixo.com";

    private const string ProtectedResource =
        """{"resource":"https://nx-dev.velixo.com/mcp","authorization_servers":["https://nx-dev.velixo.com"]}""";

    [Fact]
    public async Task A_sign_in_without_a_terminal_prints_the_code_and_the_command_that_waits_for_it()
    {
        using var test = new TestServices();
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(DeviceAnswer());

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--no-browser");

        Assert.Equal(ExitCodes.Success, exit);
        JsonNode begun = JsonNode.Parse(test.Console.StdOut)!;
        Assert.Equal("authorization_required", (string?)begun["status"]);
        Assert.Equal("WXYZ-1234", (string?)begun["userCode"]);
        Assert.Equal("https://nx-dev.velixo.com/device", (string?)begun["verificationUri"]);
        Assert.Equal("https://nx-dev.velixo.com/device?code=WXYZ-1234", (string?)begun["verificationUriComplete"]);
        Assert.Equal("velixo login --profile nx-dev --complete", (string?)begun["nextStep"]);

        RecordedRequest start = test.Http.Requests[2];
        Assert.Equal("https://nx-dev.velixo.com/oauth/device_authorization", start.Uri.ToString());
        var form = HttpUtility.ParseQueryString(start.Body!);
        Assert.Equal(GatewaySignIn.FirstPartyClientId, form["client_id"]);
        Assert.Equal("Mcp.Access", form["scope"]);
        Assert.Equal("https://nx-dev.velixo.com/mcp", form["resource"]);
        Assert.Equal("device-1", test.Services.Profiles.LoadState("nx-dev").PendingLogin!.DeviceCode);
    }

    [Fact]
    public async Task The_complete_command_waits_until_the_person_has_signed_in()
    {
        using var test = new TestServices();
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(DeviceAnswer());
        await test.RunAsync("login", "--env", "nx-dev", "--no-browser");
        test.Console.OutWriter.GetStringBuilder().Clear();
        test.Http
            .ThenJson(PollError("authorization_pending"), HttpStatusCode.BadRequest)
            .ThenJson(PollError("authorization_pending"), HttpStatusCode.BadRequest)
            .ThenJson(TokenAnswer("dev@example.com", "org-42"));

        var exit = await test.RunAsync("login", "--profile", "nx-dev", "--complete");

        Assert.Equal(ExitCodes.Success, exit);
        JsonNode signedIn = JsonNode.Parse(test.Console.StdOut)!;
        Assert.Equal("dev@example.com", (string?)signedIn["user"]!["email"]);
        Assert.Equal("nx-dev", (string?)signedIn["profile"]);
        Assert.Equal(6, test.Http.Requests.Count);

        var poll = HttpUtility.ParseQueryString(test.Http.Requests[5].Body!);
        Assert.Equal("https://nx-dev.velixo.com/oauth/token", test.Http.Requests[5].Uri.ToString());
        Assert.Equal(OAuthTokenClient.DeviceCodeGrantType, poll["grant_type"]);
        Assert.Equal("device-1", poll["device_code"]);
        Assert.Equal(GatewaySignIn.FirstPartyClientId, poll["client_id"]);
        Assert.Equal("https://nx-dev.velixo.com/mcp", poll["resource"]);

        Assert.Equal("refresh-1", test.Services.Tokens.Get("nx-dev")!.RefreshToken);
        Assert.Equal("nx-dev", test.Services.Profiles.LoadConfig().DefaultProfile);
        Assert.Null(test.Services.Profiles.LoadState("nx-dev").PendingLogin);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(test.Paths.CredentialsFile));
        }
    }

    [Fact]
    public async Task The_complete_command_finds_the_sign_in_another_profile_started()
    {
        using var test = new TestServices();
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(DeviceAnswer());
        await test.RunAsync("login", "--env", "nx-dev", "--no-browser");
        test.Console.OutWriter.GetStringBuilder().Clear();
        test.Http.ThenJson(TokenAnswer("dev@example.com", "org-42"));

        var exit = await test.RunAsync("login", "--complete");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("nx-dev", (string?)JsonNode.Parse(test.Console.StdOut)!["profile"]);
        Assert.Equal("refresh-1", test.Services.Tokens.Get("nx-dev")!.RefreshToken);
        Assert.Null(test.Services.Tokens.Get("prod"));
    }

    [Fact]
    public async Task The_complete_command_names_the_profiles_when_several_started_a_sign_in()
    {
        using var test = new TestServices();
        foreach (var profile in new[] { "nx-dev", "nx-staging" })
        {
            test.Services.Profiles.UpdateState(profile, state => state.PendingLogin = new PendingLogin
            {
                DeviceCode = "device-" + profile,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            });
        }

        var exit = await test.RunAsync("login", "--complete");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("nx-dev, nx-staging", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Empty(test.Http.Requests);
    }

    [Fact]
    public async Task The_wait_goes_on_through_a_network_failure_and_a_server_error()
    {
        using var test = new TestServices();
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(DeviceAnswer());
        await test.RunAsync("login", "--env", "nx-dev", "--no-browser");
        test.Console.OutWriter.GetStringBuilder().Clear();
        test.Http
            .Then(_ => throw new HttpRequestException("Connection reset"))
            .ThenJson("""{"error":"temporarily_unavailable"}""", HttpStatusCode.BadGateway)
            .ThenJson(TokenAnswer("dev@example.com", "org-42"));

        var exit = await test.RunAsync("login", "--profile", "nx-dev", "--complete");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("refresh-1", test.Services.Tokens.Get("nx-dev")!.RefreshToken);
        Assert.Contains("trying again", test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_slow_down_is_kept_for_a_later_complete_command()
    {
        using var test = new TestServices();
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(DeviceAnswer());
        await test.RunAsync("login", "--env", "nx-dev", "--no-browser");
        Assert.Equal(1, test.Services.Profiles.LoadState("nx-dev").PendingLogin!.IntervalSeconds);
        long? keptInterval = null;
        test.Http
            .ThenStatus(HttpStatusCode.TooManyRequests)
            .Then(_ =>
            {
                keptInterval = test.Services.Profiles.LoadState("nx-dev").PendingLogin?.IntervalSeconds;
                return FakeHttpHandler.Json(PollError("access_denied"), HttpStatusCode.BadRequest);
            });

        var exit = await test.RunAsync("login", "--profile", "nx-dev", "--complete");

        Assert.Equal(ExitCodes.AuthenticationRequired, exit);
        Assert.Equal(6, keptInterval);
    }

    [Fact]
    public async Task A_rate_limited_poll_counts_as_slow_down()
    {
        var handler = new FakeHttpHandler().ThenStatus(HttpStatusCode.TooManyRequests);
        using var http = new HttpClient(handler);

        (TokenResponse? tokens, var error, _) = await new OAuthTokenClient(http)
            .PollDeviceCodeAsync("https://nx-dev.velixo.com/oauth/token", "velixo-cli", "device-1", null, TestContext.Current.CancellationToken);

        Assert.Null(tokens);
        Assert.Equal("slow_down", error);
    }

    [Fact]
    public async Task A_no_browser_sign_in_with_only_stdout_redirected_still_waits_at_the_terminal()
    {
        using var test = new TestServices(redirected: false, outputRedirected: true);
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(DeviceAnswer())
            .ThenJson(TokenAnswer("dev@example.com", "org-42"));

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--no-browser");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Check that the page shows the code WXYZ-1234", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Equal("dev@example.com", (string?)JsonNode.Parse(test.Console.StdOut)!["user"]!["email"]);
    }

    [Theory]
    [InlineData("helper", new[] { "helper", "URL" })]
    [InlineData("code --openExternal", new[] { "code", "--openExternal", "URL" })]
    [InlineData("firefox -new-tab %s", new[] { "firefox", "-new-tab", "URL" })]
    public void The_browser_variable_names_the_command_that_opens_the_page(string browser, string[] expected)
    {
        var url = new Uri("https://nx-dev.velixo.com/oauth/authorize?a=1");

        System.Diagnostics.ProcessStartInfo start = Assert.Single(BrowserOpener.ChosenBrowserCommands(browser, url));

        Assert.Equal(expected.Select(word => word == "URL" ? url.AbsoluteUri : word), [start.FileName, .. start.ArgumentList]);
    }

    [Fact]
    public void A_browser_variable_lists_its_commands_in_order_and_an_empty_one_names_none()
    {
        var url = new Uri("https://nx-dev.velixo.com/");

        Assert.Equal(["first", "second"], BrowserOpener.ChosenBrowserCommands($"first{Path.PathSeparator}second", url).Select(start => start.FileName));
        Assert.Empty(BrowserOpener.ChosenBrowserCommands(null, url));
        Assert.Empty(BrowserOpener.ChosenBrowserCommands(" ", url));
    }

    [Fact]
    public async Task A_no_browser_sign_in_at_a_terminal_shows_the_code_and_waits()
    {
        using var test = new TestServices(redirected: false);
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(DeviceAnswer())
            .ThenJson(PollError("authorization_pending"), HttpStatusCode.BadRequest)
            .ThenJson(TokenAnswer("dev@example.com", "org-42"));

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--no-browser");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("https://nx-dev.velixo.com/device?code=WXYZ-1234", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("Check that the page shows the code WXYZ-1234", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("Signed in as dev@example.com", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Null(test.Services.Profiles.LoadState("nx-dev").PendingLogin);
    }

    [Fact]
    public async Task A_server_without_the_complete_verification_uri_gets_the_code_typed_in()
    {
        using var test = new TestServices(redirected: false);
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(DeviceAnswer(complete: null))
            .ThenJson(TokenAnswer("dev@example.com", "org-42"));

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--no-browser");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("  https://nx-dev.velixo.com/device\n", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("Enter the code WXYZ-1234, and sign in.", test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_no_browser_sign_in_marked_non_interactive_does_not_wait_at_a_terminal()
    {
        using var test = new TestServices(redirected: false);
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(DeviceAnswer());

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--no-browser", "--non-interactive");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("Waiting for the sign-in", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("velixo login --profile nx-dev --complete", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Contains("Check that the page shows the code WXYZ-1234", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Equal(3, test.Http.Requests.Count);
    }

    [Fact]
    public async Task A_sign_in_at_a_terminal_without_a_browser_uses_the_device_code()
    {
        var browser = new UnavailableBrowser("this is an SSH session");
        using var test = new TestServices(redirected: false, browser: _ => browser);
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(DeviceAnswer())
            .ThenJson(TokenAnswer("dev@example.com", "org-42"));

        var exit = await test.RunAsync("login", "--env", "nx-dev");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(browser.Opened);
        Assert.Contains("No browser can be opened here: this is an SSH session.", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("Check that the page shows the code WXYZ-1234", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("Signed in as dev@example.com", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sign_in_declined_in_the_browser_ends_the_wait()
    {
        using var test = new TestServices();
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(DeviceAnswer());
        await test.RunAsync("login", "--env", "nx-dev", "--no-browser");
        test.Http.ThenJson(PollError("access_denied"), HttpStatusCode.BadRequest);

        var exit = await test.RunAsync("login", "--profile", "nx-dev", "--complete");

        Assert.Equal(ExitCodes.AuthenticationRequired, exit);
        Assert.Contains("declined", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Null(test.Services.Profiles.LoadState("nx-dev").PendingLogin);
        Assert.Null(test.Services.Tokens.Get("nx-dev"));
    }

    [Theory]
    [InlineData("expired_token")]
    [InlineData("invalid_grant")]
    public async Task A_code_the_server_no_longer_accepts_ends_the_wait_and_names_the_way_back(string error)
    {
        using var test = new TestServices();
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(DeviceAnswer());
        await test.RunAsync("login", "--env", "nx-dev", "--no-browser");
        test.Console.OutWriter.GetStringBuilder().Clear();
        test.Http.ThenJson(PollError(error), HttpStatusCode.BadRequest);

        var exit = await test.RunAsync("login", "--profile", "nx-dev", "--complete");

        Assert.Equal(ExitCodes.AuthenticationRequired, exit);
        Assert.Contains("velixo login --profile nx-dev --no-browser", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Null(test.Services.Profiles.LoadState("nx-dev").PendingLogin);
    }

    [Fact]
    public async Task A_code_past_its_expiry_is_not_polled()
    {
        using var test = new TestServices();
        test.Services.Profiles.UpdateState("nx-dev", state => state.PendingLogin = new PendingLogin
        {
            TokenEndpoint = "https://nx-dev.velixo.com/oauth/token",
            ClientId = GatewaySignIn.FirstPartyClientId,
            DeviceCode = "device-1",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--complete");

        Assert.Equal(ExitCodes.AuthenticationRequired, exit);
        Assert.Contains("expired", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Empty(test.Http.Requests);
    }

    [Fact]
    public async Task The_complete_command_without_a_started_sign_in_is_a_usage_error()
    {
        using var test = new TestServices();

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--complete");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(test.Http.Requests);
    }

    [Fact]
    public async Task A_server_without_a_device_authorization_endpoint_says_so()
    {
        using var test = new TestServices();
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServerWithoutDeviceCode);

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--no-browser");

        Assert.Equal(ExitCodes.Failure, exit);
        Assert.Contains("device_authorization_endpoint", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Equal(2, test.Http.Requests.Count);
    }

    [Fact]
    public async Task The_client_id_variable_names_the_device_code_client()
    {
        using var test = new TestServices(new Dictionary<string, string> { ["VELIXO_CLIENT_ID"] = "configured-client" });
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(DeviceAnswer());

        var exit = await test.RunAsync("login", "--env", "nx-dev", "--no-browser");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("configured-client", HttpUtility.ParseQueryString(test.Http.Requests[2].Body!)["client_id"]);
    }

    [Fact]
    public async Task A_browser_sign_in_registers_a_client_where_the_server_does_not_know_the_first_party_one()
    {
        using TestServices signedIn = BrowserSignIn(out SigningInBrowser browser);
        signedIn.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson("""{"error":"invalid_client"}""", HttpStatusCode.BadRequest)
            .ThenJson("""{"client_id":"dcr_test"}""", HttpStatusCode.Created)
            .ThenJson(TokenAnswer("dev@example.com", "org-42"));

        var exit = await signedIn.RunAsync("login", "--env", "nx-dev");

        Assert.Equal(ExitCodes.Success, exit);
        var authorize = HttpUtility.ParseQueryString(browser.Opened!.Query);
        Assert.Equal("dcr_test", authorize["client_id"]);
        Assert.Equal("S256", authorize["code_challenge_method"]);
        Assert.StartsWith("http://127.0.0.1:", authorize["redirect_uri"], StringComparison.Ordinal);
        JsonNode registration = JsonNode.Parse(signedIn.Http.Requests[3].Body!)!;
        Assert.Equal("Velixo CLI", (string?)registration["client_name"]);
        Assert.Equal("http://127.0.0.1/callback", (string?)registration["redirect_uris"]![0]);

        var exchange = HttpUtility.ParseQueryString(signedIn.Http.Requests[4].Body!);
        Assert.Equal("authorization_code", exchange["grant_type"]);
        Assert.Equal("the-code", exchange["code"]);
        Assert.Equal(authorize["redirect_uri"], exchange["redirect_uri"]);
        Assert.Equal(authorize["code_challenge"], Pkce.ComputeChallenge(exchange["code_verifier"]!));
        Assert.Equal("dcr_test", signedIn.Services.Tokens.GetClientId(Issuer));
        Assert.Equal("refresh-1", signedIn.Services.Tokens.Get("nx-dev")!.RefreshToken);
    }

    [Theory]
    [InlineData(BrowserPlatform.Unix, "DISPLAY=:0", true, null)]
    [InlineData(BrowserPlatform.Unix, "WAYLAND_DISPLAY=wayland-0", true, null)]
    [InlineData(BrowserPlatform.Unix, "", true, "this machine has no graphical display")]
    [InlineData(BrowserPlatform.Unix, "DISPLAY=:0", false, "xdg-open is not installed")]
    [InlineData(BrowserPlatform.Unix, "SSH_CONNECTION=1 2 3 4", true, "this is an SSH session")]
    [InlineData(BrowserPlatform.Unix, "SSH_CONNECTION=1 2 3 4;DISPLAY=localhost:10.0", true, null)]
    [InlineData(BrowserPlatform.Unix, "BROWSER=/vscode/helper", false, null)]
    [InlineData(BrowserPlatform.MacOS, "", false, null)]
    [InlineData(BrowserPlatform.MacOS, "SSH_TTY=/dev/ttys001", false, "this is an SSH session")]
    [InlineData(BrowserPlatform.Windows, "", false, null)]
    [InlineData(BrowserPlatform.Windows, "SSH_CLIENT=1 2 3;DISPLAY=:0", false, "this is an SSH session")]
    public void Browser_detection_reads_the_session(BrowserPlatform platform, string variables, bool xdgOpen, string? reason)
    {
        var values = variables
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

        Assert.Equal(reason, BrowserDetection.UnavailableReason(values.GetValueOrDefault, platform, program => xdgOpen && program == "xdg-open"));
    }

    [Fact]
    public void Browser_detection_finds_a_program_on_the_path()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "xdg-open"), string.Empty);
        var path = string.Join(Path.PathSeparator, "/no/such/directory", directory.Path);

        Assert.True(BrowserDetection.IsOnPath("xdg-open", path));
        Assert.False(BrowserDetection.IsOnPath("xdg-open", "/no/such/directory"));
        Assert.False(BrowserDetection.IsOnPath("xdg-open", null));
    }

    [Fact]
    public async Task A_server_that_knows_the_first_party_client_gets_it_and_the_browser_opens_its_redirect()
    {
        using var test = BrowserSignIn(out SigningInBrowser browser);
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .Then(_ => Redirect("/signin?request=pending-1"))
            .ThenJson(TokenAnswer("dev@example.com", "org-42"));

        var exit = await test.RunAsync("login", "--env", "nx-dev");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("https://nx-dev.velixo.com/signin?request=pending-1", browser.Opened!.ToString());
        Assert.Equal(GatewaySignIn.FirstPartyClientId, HttpUtility.ParseQueryString(test.Http.Requests[2].Uri.Query)["client_id"]);
        Assert.Equal(GatewaySignIn.FirstPartyClientId, RedeemingClientId(test));
        Assert.Equal(GatewaySignIn.FirstPartyClientId, test.Services.Tokens.GetClientId(Issuer));
    }

    [Fact]
    public async Task A_stored_registered_client_gives_way_to_the_first_party_client_where_the_server_knows_it()
    {
        using var test = BrowserSignIn(out _);
        test.Services.Tokens.SaveClientId(Issuer, "dcr_stored", DateTimeOffset.UtcNow);
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .Then(_ => Redirect("/signin?request=pending-2"))
            .ThenJson(TokenAnswer("dev@example.com", "org-42"));

        var exit = await test.RunAsync("login", "--env", "nx-dev");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(GatewaySignIn.FirstPartyClientId, RedeemingClientId(test));
        Assert.Equal(GatewaySignIn.FirstPartyClientId, test.Services.Tokens.GetClientId(Issuer));
    }

    [Fact]
    public async Task A_stored_registered_client_is_reused_where_the_server_does_not_know_the_first_party_client()
    {
        using var test = BrowserSignIn(out _);
        test.Services.Tokens.SaveClientId(Issuer, "dcr_stored", DateTimeOffset.UtcNow);
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson("""{"error":"invalid_client"}""", HttpStatusCode.BadRequest)
            .ThenJson(TokenAnswer("dev@example.com", "org-42"));

        var exit = await test.RunAsync("login", "--env", "nx-dev");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(4, test.Http.Requests.Count);
        Assert.Equal("dcr_stored", RedeemingClientId(test));
        Assert.Equal("dcr_stored", test.Services.Tokens.GetClientId(Issuer));
    }

    [Fact]
    public async Task A_stored_first_party_client_is_probed_again_and_falls_back_to_a_registration()
    {
        using var test = BrowserSignIn(out _);
        test.Services.Tokens.SaveClientId(Issuer, GatewaySignIn.FirstPartyClientId, DateTimeOffset.UtcNow);
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson("""{"error":"invalid_client"}""", HttpStatusCode.BadRequest)
            .ThenJson("""{"client_id":"dcr_new"}""", HttpStatusCode.Created)
            .ThenJson(TokenAnswer("dev@example.com", "org-42"));

        var exit = await test.RunAsync("login", "--env", "nx-dev");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("dcr_new", RedeemingClientId(test));
        Assert.Equal("dcr_new", test.Services.Tokens.GetClientId(Issuer));
    }

    [Fact]
    public async Task The_client_id_variable_skips_the_probe()
    {
        using var test = BrowserSignIn(out _, new Dictionary<string, string> { ["VELIXO_CLIENT_ID"] = "configured-client" });
        test.Http
            .ThenJson(ProtectedResource)
            .ThenJson(AuthorizationServer)
            .ThenJson(TokenAnswer("dev@example.com", "org-42"));

        var exit = await test.RunAsync("login", "--env", "nx-dev");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(3, test.Http.Requests.Count);
        Assert.Equal("configured-client", RedeemingClientId(test));
    }

    /// <summary>A terminal with a browser that signs in, for the loopback sign-in.</summary>
    private static TestServices BrowserSignIn(out SigningInBrowser browser, Dictionary<string, string>? variables = null)
    {
        SigningInBrowser? created = null;
        var test = new TestServices(variables, redirected: false, browser: http => created = new SigningInBrowser(http));
        browser = created!;
        return test;
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
         "revocation_endpoint":"https://nx-dev.velixo.com/oauth/revoke",
         "device_authorization_endpoint":"https://nx-dev.velixo.com/oauth/device_authorization"}
        """;

    private const string AuthorizationServerWithoutDeviceCode =
        """
        {"issuer":"https://nx-dev.velixo.com","authorization_endpoint":"https://nx-dev.velixo.com/oauth/authorize",
         "token_endpoint":"https://nx-dev.velixo.com/oauth/token"}
        """;

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.Relative);
        return response;
    }

    private static string DeviceAnswer(string? complete = "https://nx-dev.velixo.com/device?code=WXYZ-1234") =>
        new JsonObject
        {
            ["device_code"] = "device-1",
            ["user_code"] = "WXYZ-1234",
            ["verification_uri"] = "https://nx-dev.velixo.com/device",
            ["verification_uri_complete"] = complete,
            ["expires_in"] = 600,
            ["interval"] = 0,
        }.ToJsonString();

    private static string PollError(string error) => $$"""{"error":"{{error}}"}""";

    /// <summary>Returns the client id the sign-in redeemed its code or device code with.</summary>
    private static string? RedeemingClientId(TestServices test) =>
        HttpUtility.ParseQueryString(test.Http.Requests[^1].Body!)["client_id"];

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

    private sealed class UnavailableBrowser(string reason) : IBrowserOpener
    {
        public bool Opened { get; private set; }

        public string? UnavailableReason => reason;

        public bool TryOpen(Uri url) => Opened = true;
    }

    /// <summary>
    /// Stands in for a person who signs in: it sends the loopback callback the sign-in waits for, with the
    /// redirect URI and state of the authorize URL the browser opens, or of the probe the CLI sent first when
    /// the browser opens the server's own sign-in page.
    /// </summary>
    private sealed class SigningInBrowser(FakeHttpHandler http) : IBrowserOpener
    {
        public Uri? Opened { get; private set; }

        public bool TryOpen(Uri url)
        {
            Opened = url;
            var query = HttpUtility.ParseQueryString(url.Query);
            if (query["redirect_uri"] is null)
            {
                query = HttpUtility.ParseQueryString(http.Requests.Last(r => r.Uri.AbsolutePath == "/oauth/authorize").Uri.Query);
            }

            var callback = new Uri($"{query["redirect_uri"]}?code=the-code&state={Uri.EscapeDataString(query["state"]!)}");
            _ = Task.Run(async () =>
            {
                using var client = new HttpClient();
                using HttpResponseMessage response = await client.GetAsync(callback);
            });
            return true;
        }
    }
}
