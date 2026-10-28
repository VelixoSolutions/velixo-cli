using System.Text.Json.Serialization;

namespace Velixo.Cli.Serialization;

/// <summary>A successful answer from the token endpoint.</summary>
public sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    /// <summary>Gets or sets the access token's lifetime, in seconds.</summary>
    [JsonPropertyName("expires_in")]
    public long? ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("id_token")]
    public string? IdToken { get; set; }
}
