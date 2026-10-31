using System.Text.Json.Serialization;

namespace Velixo.Cli.Config;

/// <summary>One profile's settings in <c>config.json</c>. Its tokens live in <c>credentials.json</c>.</summary>
public sealed class ProfileConfig
{
    /// <summary>Gets or sets the built-in environment name, when the profile points at one.</summary>
    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    /// <summary>Gets or sets the MCP URL, when the profile points at a URL that is not a built-in environment.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("connection")]
    public string? Connection { get; set; }
}
