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

    /// <summary>The path of the endpoint the CLI calls, which a server address given without a path gets.</summary>
    public const string EndpointPath = "/mcp";

    /// <summary>
    /// Returns the endpoint of the server at <paramref name="url"/>: the URL as given when it has a path,
    /// and <see cref="EndpointPath"/> on that server when it has none, so <c>https://feature-x.velixo.com</c>
    /// and <c>https://feature-x.velixo.com/mcp</c> name the same endpoint.
    /// </summary>
    public static Uri Endpoint(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        return url.AbsolutePath is "" or "/" ? new Uri(url, EndpointPath) : url;
    }

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
