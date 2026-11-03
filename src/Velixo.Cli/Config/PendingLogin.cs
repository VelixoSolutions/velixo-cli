using System.Text.Json.Serialization;

namespace Velixo.Cli.Config;

/// <summary>
/// A device code sign-in started by <c>velixo login --no-browser</c>, kept so that
/// <c>velixo login --complete</c> in a later process can wait for the same sign-in to finish.
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

    [JsonPropertyName("deviceCode")]
    public string? DeviceCode { get; set; }

    [JsonPropertyName("userCode")]
    public string? UserCode { get; set; }

    [JsonPropertyName("verificationUri")]
    public string? VerificationUri { get; set; }

    [JsonPropertyName("verificationUriComplete")]
    public string? VerificationUriComplete { get; set; }

    /// <summary>Gets or sets the seconds to wait between two polls, as the server last asked.</summary>
    [JsonPropertyName("intervalSeconds")]
    public long IntervalSeconds { get; set; }

    [JsonPropertyName("resource")]
    public string? Resource { get; set; }

    [JsonPropertyName("mcpUrl")]
    public string? McpUrl { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }
}
