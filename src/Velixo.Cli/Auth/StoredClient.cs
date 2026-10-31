using System.Text.Json.Serialization;

namespace Velixo.Cli.Auth;

/// <summary>The OAuth client the CLI registered with one authorization server.</summary>
public sealed class StoredClient
{
    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    [JsonPropertyName("registeredAt")]
    public DateTimeOffset RegisteredAt { get; set; }
}
