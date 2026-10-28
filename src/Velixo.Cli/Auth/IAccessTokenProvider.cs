namespace Velixo.Cli.Auth;

/// <summary>Hands the MCP client a gateway access token for the current profile.</summary>
public interface IAccessTokenProvider
{
    /// <summary>A token that is good for at least another minute, refreshed first when it is not.</summary>
    /// <exception cref="Output.CliException">Exit 4 when the profile is not signed in or its session ended.</exception>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>Refreshes the token whatever its expiry, after the server refused the current one.</summary>
    Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>The refresh token, after any <c>VELIXO_TOKEN</c> has been applied; what <c>velixo auth token</c> prints.</summary>
    Task<string> GetRefreshTokenAsync(CancellationToken cancellationToken);
}
