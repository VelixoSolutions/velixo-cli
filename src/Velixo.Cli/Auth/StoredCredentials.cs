using System.Text.Json.Serialization;

namespace Velixo.Cli.Auth;

/// <summary>One profile's sign-in: where its tokens come from, the tokens, and who they name.</summary>
public sealed class StoredCredentials
{
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    [JsonPropertyName("tokenEndpoint")]
    public string? TokenEndpoint { get; set; }

    [JsonPropertyName("revocationEndpoint")]
    public string? RevocationEndpoint { get; set; }

    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    /// <summary>Gets or sets the RFC 8707 resource the tokens are for: the canonical MCP URL.</summary>
    [JsonPropertyName("resource")]
    public string? Resource { get; set; }

    [JsonPropertyName("mcpUrl")]
    public string? McpUrl { get; set; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("accessTokenExpiresAt")]
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    /// <summary>Gets or sets the refresh token. The gateway rotates it on every use, so the stored one is always the latest.</summary>
    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("signedInAt")]
    public DateTimeOffset SignedInAt { get; set; }

    /// <summary>Gets or sets when the gateway session ends at the latest, whatever the refreshes.</summary>
    [JsonPropertyName("sessionExpiresAt")]
    public DateTimeOffset? SessionExpiresAt { get; set; }

    [JsonPropertyName("user")]
    public SignedInUser? User { get; set; }

    [JsonPropertyName("organization")]
    public SignedInOrganization? Organization { get; set; }

    /// <summary>
    /// Gets or sets the SHA-256 of the <c>VELIXO_TOKEN</c> these tokens were obtained from, so a later
    /// command with the same variable uses the rotated tokens instead of replaying a spent one.
    /// </summary>
    [JsonPropertyName("environmentTokenHash")]
    public string? EnvironmentTokenHash { get; set; }
}
