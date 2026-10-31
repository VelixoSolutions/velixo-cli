using System.Text.Json.Serialization;

namespace Velixo.Cli.Serialization;

/// <summary>RFC 8414 authorization server metadata, served at <c>/.well-known/oauth-authorization-server</c>.</summary>
public sealed class AuthorizationServerMetadata
{
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    [JsonPropertyName("authorization_endpoint")]
    public string? AuthorizationEndpoint { get; set; }

    [JsonPropertyName("token_endpoint")]
    public string? TokenEndpoint { get; set; }

    [JsonPropertyName("registration_endpoint")]
    public string? RegistrationEndpoint { get; set; }

    [JsonPropertyName("revocation_endpoint")]
    public string? RevocationEndpoint { get; set; }
}
