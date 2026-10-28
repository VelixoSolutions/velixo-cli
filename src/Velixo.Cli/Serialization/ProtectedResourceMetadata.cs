using System.Text.Json.Serialization;

namespace Velixo.Cli.Serialization;

/// <summary>RFC 9728 protected-resource metadata, served at <c>/.well-known/oauth-protected-resource</c>.</summary>
public sealed class ProtectedResourceMetadata
{
    [JsonPropertyName("resource")]
    public string? Resource { get; set; }

    [JsonPropertyName("authorization_servers")]
    public List<string>? AuthorizationServers { get; set; }
}
