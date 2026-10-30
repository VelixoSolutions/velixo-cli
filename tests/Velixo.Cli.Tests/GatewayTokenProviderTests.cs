using System.Web;
using Velixo.Cli.Auth;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class GatewayTokenProviderTests
{
    [Fact]
    public async Task An_expired_access_token_is_refreshed_and_the_rotated_refresh_token_stored()
    {
        using var test = new TestServices();
        test.Services.Tokens.Save("nx-dev", new StoredCredentials
        {
            TokenEndpoint = "https://nx-dev.velixo.com/oauth/token",
            ClientId = "dcr_test",
            McpUrl = "https://nx-dev.velixo.com/mcp",
            AccessToken = "old",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-5),
            RefreshToken = "refresh-old",
            SignedInAt = DateTimeOffset.UtcNow,
        });
        test.Http.ThenJson(GatewaySignInTests.TokenAnswer("dev@example.com", "org-42", "refresh-new"));

        var token = await Provider(test, Settings()).GetAccessTokenAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual("old", token);
        var form = HttpUtility.ParseQueryString(test.Http.Requests[0].Body!);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("refresh-old", form["refresh_token"]);
        Assert.Equal("dcr_test", form["client_id"]);
        Assert.Equal("refresh-new", test.Services.Tokens.Get("nx-dev")!.RefreshToken);
    }

    [Fact]
    public async Task Velixo_token_is_spent_once_and_its_rotation_reused_by_the_next_command()
    {
        using var test = new TestServices();
        test.Services.Tokens.SaveClientId("https://nx-dev.velixo.com", "dcr_test", DateTimeOffset.UtcNow);
        test.Http
            .ThenJson("""{"resource":"https://nx-dev.velixo.com/mcp","authorization_servers":["https://nx-dev.velixo.com"]}""")
            .ThenJson(GatewaySignInTests.AuthorizationServer)
            .ThenJson(GatewaySignInTests.TokenAnswer("ci@example.com", "org-42", "refresh-rotated"));
        ResolvedSettings settings = Settings() with { EnvironmentToken = "refresh-from-env" };

        await Provider(test, settings).GetAccessTokenAsync(TestContext.Current.CancellationToken);
        var second = await Provider(test, settings).GetAccessTokenAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, test.Http.Requests.Count);
        Assert.Equal("refresh-from-env", HttpUtility.ParseQueryString(test.Http.Requests[2].Body!)["refresh_token"]);
        Assert.Equal("refresh-rotated", test.Services.Tokens.Get("nx-dev")!.RefreshToken);
        Assert.False(string.IsNullOrEmpty(second));
    }

    [Fact]
    public async Task No_stored_sign_in_is_exit_4()
    {
        using var test = new TestServices();

        CliException error = await Assert.ThrowsAsync<CliException>(() =>
            Provider(test, Settings()).GetAccessTokenAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ExitCodes.AuthenticationRequired, error.ExitCode);
        Assert.Equal(ErrorCodes.NotSignedIn, error.Code);
    }

    [Fact]
    public async Task A_refresh_another_process_finished_while_this_one_waited_is_reused_not_repeated()
    {
        using var test = new TestServices();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        test.Services.Tokens.Save("nx-dev", Credentials("refresh-old", "access-old", expiresIn: TimeSpan.FromSeconds(-5)));
        IDisposable otherProcess = await test.Services.Tokens.LockForRefreshAsync("nx-dev", cancellationToken);

        Task<string> waiting = Provider(test, Settings()).GetAccessTokenAsync(cancellationToken);
        await Task.Delay(200, cancellationToken);
        Assert.False(waiting.IsCompleted);

        test.Services.Tokens.Save("nx-dev", Credentials("refresh-rotated", "access-rotated", expiresIn: TimeSpan.FromMinutes(30)));
        otherProcess.Dispose();

        Assert.Equal("access-rotated", await waiting);
        Assert.Empty(test.Http.Requests);
    }

    [Fact]
    public async Task A_refresh_after_another_process_rotated_the_token_presents_the_rotated_one()
    {
        using var test = new TestServices();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        test.Services.Tokens.Save("nx-dev", Credentials("refresh-old", "access-old", expiresIn: TimeSpan.FromSeconds(-5)));
        test.Http.ThenJson(GatewaySignInTests.TokenAnswer("dev@example.com", "org-42", "refresh-newest"));
        IDisposable otherProcess = await test.Services.Tokens.LockForRefreshAsync("nx-dev", cancellationToken);

        Task<string> waiting = Provider(test, Settings()).GetAccessTokenAsync(cancellationToken);
        await Task.Delay(200, cancellationToken);
        test.Services.Tokens.Save("nx-dev", Credentials("refresh-rotated", "access-rotated", expiresIn: TimeSpan.FromSeconds(-5)));
        otherProcess.Dispose();
        await waiting;

        Assert.Equal("refresh-rotated", HttpUtility.ParseQueryString(test.Http.Requests.Single().Body!)["refresh_token"]);
        Assert.Equal("refresh-newest", test.Services.Tokens.Get("nx-dev")!.RefreshToken);
    }

    [Fact]
    public async Task The_refresh_lock_admits_one_holder_at_a_time()
    {
        using var test = new TestServices();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        TimeSpan promptly = TimeSpan.FromSeconds(5);

        IDisposable first = await test.Services.Tokens.LockForRefreshAsync("nx-dev", cancellationToken).WaitAsync(promptly, cancellationToken);
        Task<IDisposable> second = test.Services.Tokens.LockForRefreshAsync("nx-dev", cancellationToken);
        await Task.Delay(200, cancellationToken);
        Assert.False(second.IsCompleted);

        first.Dispose();
        (await second.WaitAsync(promptly, cancellationToken)).Dispose();
    }

    [Fact]
    public async Task A_held_refresh_lock_is_waited_for_until_the_command_is_cancelled_never_skipped()
    {
        using var test = new TestServices();
        using IDisposable otherProcess = await test.Services.Tokens.LockForRefreshAsync("nx-dev", TestContext.Current.CancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.Services.Tokens.LockForRefreshAsync("nx-dev", timeout.Token));
    }

    [Fact]
    public async Task A_refresh_on_one_profile_does_not_hold_up_another_profile()
    {
        using var test = new TestServices();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using IDisposable prod = await test.Services.Tokens.LockForRefreshAsync("prod", cancellationToken);

        using IDisposable dev = await test.Services.Tokens.LockForRefreshAsync("nx-dev", cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    [Fact]
    public async Task Velixo_token_spent_by_another_process_hands_out_that_process_rotation()
    {
        using var test = new TestServices();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        test.Services.Tokens.Save("nx-dev", Credentials("refresh-of-a-sign-in", "access-of-a-sign-in", expiresIn: TimeSpan.FromSeconds(-5)));
        ResolvedSettings settings = Settings() with { EnvironmentToken = "refresh-from-env" };
        GatewayTokenProvider provider = Provider(test, settings);
        IDisposable otherProcess = await test.Services.Tokens.LockForRefreshAsync("nx-dev", cancellationToken);

        Task<string> waiting = provider.GetRefreshTokenAsync(cancellationToken);
        await Task.Delay(200, cancellationToken);
        StoredCredentials rotated = Credentials("refresh-rotated", "access-rotated", expiresIn: TimeSpan.FromMinutes(30));
        rotated.EnvironmentTokenHash = GatewayTokenProvider.HashToken("refresh-from-env");
        test.Services.Tokens.Save("nx-dev", rotated);
        otherProcess.Dispose();

        Assert.Equal("refresh-rotated", await waiting);
        Assert.Empty(test.Http.Requests);
    }

    [Fact]
    public async Task A_new_sign_in_while_this_command_waited_ends_the_command_instead_of_adopting_it()
    {
        using var test = new TestServices();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        test.Services.Tokens.Save("nx-dev", Credentials("refresh-old", "access-old", expiresIn: TimeSpan.FromSeconds(-5)));
        IDisposable otherProcess = await test.Services.Tokens.LockForRefreshAsync("nx-dev", cancellationToken);

        Task<string> waiting = Provider(test, Settings()).GetAccessTokenAsync(cancellationToken);
        await Task.Delay(200, cancellationToken);
        StoredCredentials signedInAgain = Credentials("refresh-new-session", "access-new-session", expiresIn: TimeSpan.FromMinutes(30));
        signedInAgain.SignedInAt = DateTimeOffset.UtcNow.AddMinutes(1);
        test.Services.Tokens.Save("nx-dev", signedInAgain);
        otherProcess.Dispose();

        CliException error = await Assert.ThrowsAsync<CliException>(() => waiting);
        Assert.Equal(ErrorCodes.SessionEnded, error.Code);
        Assert.Empty(test.Http.Requests);
    }

    [Fact]
    public async Task A_sign_out_while_this_command_waited_ends_it_instead_of_storing_the_tokens_again()
    {
        using var test = new TestServices();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        test.Services.Tokens.Save("nx-dev", Credentials("refresh-old", "access-old", expiresIn: TimeSpan.FromSeconds(-5)));
        IDisposable otherProcess = await test.Services.Tokens.LockForRefreshAsync("nx-dev", cancellationToken);

        Task<string> waiting = Provider(test, Settings()).GetAccessTokenAsync(cancellationToken);
        await Task.Delay(200, cancellationToken);
        test.Services.Tokens.Delete("nx-dev");
        otherProcess.Dispose();

        CliException error = await Assert.ThrowsAsync<CliException>(() => waiting);
        Assert.Equal(ErrorCodes.NotSignedIn, error.Code);
        Assert.Empty(test.Http.Requests);
        Assert.Null(test.Services.Tokens.Get("nx-dev"));
    }

    [Fact]
    public async Task A_velixo_token_session_that_replaced_the_sign_in_while_this_command_waited_is_not_overwritten()
    {
        using var test = new TestServices();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        test.Services.Tokens.Save("nx-dev", Credentials("refresh-of-a-sign-in", "access-old", expiresIn: TimeSpan.FromSeconds(-5)));
        IDisposable otherProcess = await test.Services.Tokens.LockForRefreshAsync("nx-dev", cancellationToken);

        Task<string> waiting = Provider(test, Settings()).GetAccessTokenAsync(cancellationToken);
        await Task.Delay(200, cancellationToken);
        StoredCredentials fromEnvironment = Credentials("refresh-rotated-from-env", "access-env", expiresIn: TimeSpan.FromSeconds(-5));
        fromEnvironment.EnvironmentTokenHash = GatewayTokenProvider.HashToken("refresh-from-env");
        test.Services.Tokens.Save("nx-dev", fromEnvironment);
        otherProcess.Dispose();

        CliException error = await Assert.ThrowsAsync<CliException>(() => waiting);
        Assert.Equal(ErrorCodes.SessionEnded, error.Code);
        Assert.Empty(test.Http.Requests);
        Assert.Equal("refresh-rotated-from-env", test.Services.Tokens.Get("nx-dev")!.RefreshToken);
    }

    [Fact]
    public async Task Two_profiles_that_use_the_same_velixo_token_refresh_one_after_the_other()
    {
        using var test = new TestServices();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        test.Services.Tokens.Save("other", Credentials("refresh-of-a-sign-in", "access-old", expiresIn: TimeSpan.FromSeconds(-5)));
        test.Http.ThenJson(GatewaySignInTests.TokenAnswer("ci@example.com", "org-42", "refresh-rotated"));
        IDisposable firstProfile = await test.Services.Tokens.LockForEnvironmentTokenAsync(GatewayTokenProvider.HashToken("refresh-from-env"), cancellationToken);

        Task<string> waiting = Provider(test, Settings() with { ProfileName = "other", EnvironmentToken = "refresh-from-env" }).GetAccessTokenAsync(cancellationToken);
        await Task.Delay(200, cancellationToken);
        Assert.False(waiting.IsCompleted);
        Assert.Empty(test.Http.Requests);

        firstProfile.Dispose();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Assert.Single(test.Http.Requests);
    }

    [Fact]
    public async Task Different_velixo_tokens_do_not_hold_each_other_up()
    {
        using var test = new TestServices();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using IDisposable first = await test.Services.Tokens.LockForEnvironmentTokenAsync(GatewayTokenProvider.HashToken("token-a"), cancellationToken);

        using IDisposable second = await test.Services.Tokens
            .LockForEnvironmentTokenAsync(GatewayTokenProvider.HashToken("token-b"), cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    private static readonly DateTimeOffset _signedInAt = DateTimeOffset.UtcNow;

    private static StoredCredentials Credentials(string refreshToken, string accessToken, TimeSpan expiresIn) => new()
    {
        TokenEndpoint = "https://nx-dev.velixo.com/oauth/token",
        ClientId = "dcr_test",
        McpUrl = "https://nx-dev.velixo.com/mcp",
        AccessToken = accessToken,
        AccessTokenExpiresAt = DateTimeOffset.UtcNow + expiresIn,
        RefreshToken = refreshToken,
        SignedInAt = _signedInAt,
    };

    private static ResolvedSettings Settings() => new()
    {
        ProfileName = "nx-dev",
        McpUrl = new Uri("https://nx-dev.velixo.com/mcp"),
        EnvironmentName = "nx-dev",
        Output = new OutputSettings(),
    };

    private static GatewayTokenProvider Provider(TestServices test, ResolvedSettings settings) =>
        new(test.Services.Tokens, new OAuthTokenClient(test.Services.Http), new OAuthDiscovery(test.Services.Http), settings, TimeProvider.System);
}
