using System.Text.Json.Serialization;

namespace Velixo.Cli.Config;

/// <summary>
/// A sign-in started by <c>velixo login --no-browser</c>, kept so that <c>velixo login --complete</c>
/// in a later process can redeem the code with the same PKCE verifier and check the same state.
/// </summary>
public sealed class PendingLogin
{
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    [JsonPropertyName("tokenEndpoint")]
    public string? TokenEndpoint { get; set; }

    [JsonPropertyName("revocationEndpoint")]
    public string? RevocationEndpoint { get; set; }

    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    [JsonPropertyName("redirectUri")]
    public string? RedirectUri { get; set; }

    [JsonPropertyName("codeVerifier")]
    public string? CodeVerifier { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("resource")]
    public string? Resource { get; set; }

    [JsonPropertyName("mcpUrl")]
    public string? McpUrl { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }
}
