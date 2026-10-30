namespace Velixo.Cli.Auth;

/// <summary>Reads and writes <c>credentials.json</c>. Nothing else in the CLI touches tokens on disk.</summary>
public interface ITokenStore
{
    StoredCredentials? Get(string profile);

    void Save(string profile, StoredCredentials credentials);

    void Delete(string profile);

    IReadOnlyList<string> ListProfiles();

    /// <summary>The client id registered with the authorization server <paramref name="issuer"/>, or null.</summary>
    string? GetClientId(string issuer);

    void SaveClientId(string issuer, string clientId, DateTimeOffset registeredAt);

    /// <summary>
    /// Waits until no other velixo process on this machine is refreshing, replacing or deleting the tokens of
    /// <paramref name="profile"/>, then keeps them waiting until the returned lock is disposed. Read the stored
    /// credentials again after it is taken.
    /// </summary>
    Task<IDisposable> LockForRefreshAsync(string profile, CancellationToken cancellationToken);
}
