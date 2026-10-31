using Velixo.Cli.Auth;

namespace Velixo.Cli.Tests.Fakes;

internal sealed class FakeAccessTokens : IAccessTokenProvider
{
    public int Refreshes { get; private set; }

    public string Current { get; private set; } = "access-1";

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

    public Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        Refreshes++;
        Current = $"access-{Refreshes + 1}";
        return Task.FromResult(Current);
    }

    public Task<string> GetRefreshTokenAsync(CancellationToken cancellationToken) => Task.FromResult("refresh-1");
}
