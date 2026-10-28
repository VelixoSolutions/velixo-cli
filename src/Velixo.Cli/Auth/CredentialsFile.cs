using System.Text.Json.Serialization;

namespace Velixo.Cli.Auth;

/// <summary>The contents of <c>credentials.json</c>: the registered OAuth clients and each profile's tokens.</summary>
public sealed class CredentialsFile
{
    /// <summary>Gets or sets the client registered with each authorization server, keyed by its issuer.</summary>
    [JsonPropertyName("clients")]
    public Dictionary<string, StoredClient> Clients { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("profiles")]
    public Dictionary<string, StoredCredentials> Profiles { get; set; } = new(StringComparer.Ordinal);
}
