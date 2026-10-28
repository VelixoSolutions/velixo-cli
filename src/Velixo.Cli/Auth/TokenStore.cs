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
    public StoredCredentials? Get(string profile) => Load().Profiles.GetValueOrDefault(profile);

    public void Save(string profile, StoredCredentials credentials)
    {
        CredentialsFile file = Load();
        file.Profiles[profile] = credentials;
        Write(file);
    }

    public void Delete(string profile)
    {
        CredentialsFile file = Load();
        if (file.Profiles.Remove(profile))
        {
            Write(file);
        }
    }

    public IReadOnlyList<string> ListProfiles() => Load().Profiles.Keys.Order(StringComparer.Ordinal).ToList();

    public string? GetClientId(string issuer) => Load().Clients.GetValueOrDefault(issuer)?.ClientId;

    public void SaveClientId(string issuer, string clientId, DateTimeOffset registeredAt)
    {
        CredentialsFile file = Load();
        file.Clients[issuer] = new StoredClient { ClientId = clientId, RegisteredAt = registeredAt };
        Write(file);
    }

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
                "CREDENTIALS_INVALID",
                $"{paths.CredentialsFile} is not valid JSON: {exception.Message}",
                "Delete the file and sign in again with 'velixo login'.");
        }
    }

    private void Write(CredentialsFile file) =>
        JsonFile.Write(paths.CredentialsFile, file, CliJsonContext.Default.CredentialsFile, ownerOnly: true);
}
