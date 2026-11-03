using System.Text.Json.Serialization;

namespace Velixo.Cli.Serialization;

/// <summary>An RFC 8628 §3.2 answer from the device authorization endpoint.</summary>
public sealed class DeviceAuthorizationResponse
{
    [JsonPropertyName("device_code")]
    public string? DeviceCode { get; set; }

    [JsonPropertyName("user_code")]
    public string? UserCode { get; set; }

    [JsonPropertyName("verification_uri")]
    public string? VerificationUri { get; set; }

    /// <summary>Gets or sets the verification URI with the user code already in it, when the server offers one.</summary>
    [JsonPropertyName("verification_uri_complete")]
    public string? VerificationUriComplete { get; set; }

    /// <summary>Gets or sets how long the codes stay valid, in seconds.</summary>
    [JsonPropertyName("expires_in")]
    public long? ExpiresIn { get; set; }

    /// <summary>Gets or sets the seconds to wait between two polls of the token endpoint.</summary>
    [JsonPropertyName("interval")]
    public long? Interval { get; set; }
}
