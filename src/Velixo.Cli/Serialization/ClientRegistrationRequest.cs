using System.Text.Json.Serialization;

namespace Velixo.Cli.Serialization;

/// <summary>An RFC 7591 dynamic client registration request.</summary>
public sealed class ClientRegistrationRequest
{
    [JsonPropertyName("client_name")]
    required public string ClientName { get; init; }

    [JsonPropertyName("redirect_uris")]
    required public IReadOnlyList<string> RedirectUris { get; init; }

    [JsonPropertyName("grant_types")]
    required public IReadOnlyList<string> GrantTypes { get; init; }

    [JsonPropertyName("response_types")]
    required public IReadOnlyList<string> ResponseTypes { get; init; }

    [JsonPropertyName("token_endpoint_auth_method")]
    required public string TokenEndpointAuthMethod { get; init; }

    [JsonPropertyName("scope")]
    required public string Scope { get; init; }
}
