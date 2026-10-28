using System.Text.Json.Serialization;

namespace Velixo.Cli.Config;

/// <summary>A fingerprint of a write the CLI ran, kept for the duplicate guard.</summary>
public sealed class RecentWrite
{
    [JsonPropertyName("fingerprint")]
    public string? Fingerprint { get; set; }

    [JsonPropertyName("executedAt")]
    public DateTimeOffset ExecutedAt { get; set; }
}
