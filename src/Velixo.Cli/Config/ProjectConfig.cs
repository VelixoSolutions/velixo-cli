using System.Text.Json.Serialization;

namespace Velixo.Cli.Config;

/// <summary>
/// The contents of <c>.velixo.json</c>, the project defaults found in the working directory or a
/// parent. It never holds credentials.
/// </summary>
public sealed class ProjectConfig
{
    public const string FileName = ".velixo.json";

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("connection")]
    public string? Connection { get; set; }
}
