using System.Text.Json;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Serialization;

namespace Velixo.Cli.Auth;

/// <summary>
/// Keeps the tokens in <c>credentials.json</c>, created with mode 0600 on Unix. The OS credential
/// store the spec prefers is a later step; this is its file fallback.
/// </summary>
public sealed class TokenStore(ConfigPaths paths) : ITokenStore
{
    // A refresh is one HTTP call, which the HTTP client gives up on after 100 seconds.
    private static readonly TimeSpan _refreshLockWait = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _updateLockWait = TimeSpan.FromSeconds(30);

    public StoredCredentials? Get(string profile) => Load().Profiles.GetValueOrDefault(profile);

    public void Save(string profile, StoredCredentials credentials) =>
        Update(file => file.Profiles[profile] = credentials);

    public void Delete(string profile) => Update(file => file.Profiles.Remove(profile));

    public IReadOnlyList<string> ListProfiles() => Load().Profiles.Keys.Order(StringComparer.Ordinal).ToList();

    public string? GetClientId(string issuer) => Load().Clients.GetValueOrDefault(issuer)?.ClientId;

    public void SaveClientId(string issuer, string clientId, DateTimeOffset registeredAt) =>
        Update(file => file.Clients[issuer] = new StoredClient { ClientId = clientId, RegisteredAt = registeredAt });

    /// <summary>Changes the file under its lock, so that two processes saving different profiles both keep theirs.</summary>
    private void Update(Action<CredentialsFile> change)
    {
        using IDisposable fileLock = FileLock.Acquire(ConfigPaths.UpdateLockFile(paths.CredentialsFile), _updateLockWait);
        CredentialsFile file = Load();
        change(file);
        Write(file);
    }

    public Task<IDisposable> LockForRefreshAsync(CancellationToken cancellationToken) =>
        FileLock.AcquireAsync(paths.TokenRefreshLockFile, _refreshLockWait, cancellationToken);

    private CredentialsFile Load()
    {
        try
        {
            return JsonFile.Read(paths.CredentialsFile, CliJsonContext.Default.CredentialsFile) ?? new CredentialsFile();
        }
        catch (JsonException exception)
        {
            throw new CliException(
                ExitCodes.AuthenticationRequired,
                ErrorCodes.CredentialsInvalid,
                $"{paths.CredentialsFile} is not valid JSON: {exception.Message}",
                "Delete the file and sign in again with 'velixo login'.");
        }
    }

    private void Write(CredentialsFile file) =>
        JsonFile.Write(paths.CredentialsFile, file, CliJsonContext.Default.CredentialsFile, ownerOnly: true);
}
