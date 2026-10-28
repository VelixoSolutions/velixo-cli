using System.Text.Json.Serialization;

namespace Velixo.Cli.Config;

/// <summary>The <c>serverInfo</c> the server answered <c>initialize</c> with.</summary>
public sealed class McpServerInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
