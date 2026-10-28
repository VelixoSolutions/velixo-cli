using System.Text.Json.Serialization;

namespace Velixo.Cli.Serialization;

/// <summary>An RFC 6749 §5.2 error answer.</summary>
public sealed class OAuthErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}
