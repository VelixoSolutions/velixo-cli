using System.Text.Json.Serialization;

namespace Velixo.Cli.Config;

/// <summary>The contents of <c>config.json</c>: the default profile, the default output and each profile's environment.</summary>
public sealed class CliConfig
{
    [JsonPropertyName("defaultProfile")]
    public string? DefaultProfile { get; set; }

    [JsonPropertyName("defaultOutput")]
    public string? DefaultOutput { get; set; }

    [JsonPropertyName("profiles")]
    public Dictionary<string, ProfileConfig> Profiles { get; set; } = new(StringComparer.Ordinal);
}
