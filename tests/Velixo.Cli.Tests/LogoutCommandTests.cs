using System.Net;
using System.Web;
using Velixo.Cli.Auth;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class LogoutCommandTests
{
    private const string RevocationEndpoint = "https://nx-dev.velixo.com/oauth/revoke";

    [Fact]
    public async Task A_logout_waits_for_a_refresh_in_flight_then_revokes_the_rotated_token_and_deletes_it()
    {
        using var test = new TestServices();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        test.Services.Tokens.Save("nx-dev", Credentials("refresh-old"));
        for (var i = 0; i < 20; i++)
        {
            test.Http.Then(request => request.RequestUri!.ToString() == RevocationEndpoint
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        IDisposable refreshInFlight = await test.Services.Tokens.LockForRefreshAsync("nx-dev", cancellationToken);
        Task<int> logout = test.RunAsync("logout", "--profile", "nx-dev");
        await Task.Delay(500, cancellationToken);
        Assert.NotNull(test.Services.Tokens.Get("nx-dev"));

        test.Services.Tokens.Save("nx-dev", Credentials("refresh-rotated"));
        refreshInFlight.Dispose();

        Assert.Equal(ExitCodes.Success, await logout.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken));
        RecordedRequest revocation = Assert.Single(test.Http.Requests, request => request.Uri.ToString() == RevocationEndpoint);
        Assert.Equal("refresh-rotated", HttpUtility.ParseQueryString(revocation.Body!)["token"]);
        Assert.Null(test.Services.Tokens.Get("nx-dev"));
    }

    private static StoredCredentials Credentials(string refreshToken) => new()
    {
        TokenEndpoint = "https://nx-dev.velixo.com/oauth/token",
        RevocationEndpoint = RevocationEndpoint,
        ClientId = "dcr_test",
        McpUrl = "https://nx-dev.velixo.com/mcp",
        AccessToken = "access",
        AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
        RefreshToken = refreshToken,
        SignedInAt = DateTimeOffset.UtcNow,
    };
}
