using System.Text.Json.Serialization;

namespace Velixo.Cli.Serialization;

/// <summary>The part of GitHub's <c>releases/latest</c> answer that <c>velixo version --check</c> reads.</summary>
public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }
}
