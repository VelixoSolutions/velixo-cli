namespace Velixo.Cli.Config;

/// <summary>
/// The built-in Velixo environments and their MCP endpoints, the same URLs the Velixo plugins
/// connect to.
/// </summary>
public static class Environments
{
    public const string Prod = "prod";
    public const string NxStaging = "nx-staging";
    public const string NxStageZero = "nx-stage-zero";
    public const string NxDev = "nx-dev";
    public const string Local = "local";

    public const string Default = Prod;

    public static readonly IReadOnlyDictionary<string, Uri> McpUrls = new Dictionary<string, Uri>(StringComparer.Ordinal)
    {
        [Prod] = new("https://app.velixo.com/mcp"),
        [NxStaging] = new("https://nx-staging.velixo.com/mcp"),
        [NxStageZero] = new("https://nx-stage-zero.velixo.com/mcp"),
        [NxDev] = new("https://nx-dev.velixo.com/mcp"),
        [Local] = new("https://nx-local.velixo.com/mcp"),
    };

    public static bool TryGetUrl(string? name, out Uri url)
    {
        if (name is not null && McpUrls.TryGetValue(name, out Uri? found))
        {
            url = found;
            return true;
        }

        url = McpUrls[Default];
        return false;
    }

    /// <summary>The built-in environment whose MCP URL is <paramref name="url"/>, or null.</summary>
    public static string? NameOf(Uri url) =>
        McpUrls.FirstOrDefault(pair => UrlsMatch(pair.Value, url)).Key;

    private static bool UrlsMatch(Uri left, Uri right) =>
        string.Equals(left.GetLeftPart(UriPartial.Authority), right.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.AbsolutePath.TrimEnd('/'), right.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal);
}
