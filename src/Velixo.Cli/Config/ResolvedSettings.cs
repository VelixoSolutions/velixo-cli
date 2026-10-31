using Velixo.Cli.Output;

namespace Velixo.Cli.Config;

/// <summary>The settings one command runs with, after every source has been applied in order.</summary>
public sealed record ResolvedSettings
{
    required public string ProfileName { get; init; }

    /// <summary>Gets the built-in environment the MCP URL belongs to, or null for any other URL.</summary>
    public string? EnvironmentName { get; init; }

    required public Uri McpUrl { get; init; }

    public string? Connection { get; init; }

    public string? Intent { get; init; }

    required public OutputSettings Output { get; init; }

    /// <summary>Gets the AI agent the CLI runs under, from its environment variables, or null.</summary>
    public string? Agent { get; init; }

    /// <summary>Gets a value indicating whether the CLI may prompt: no <c>--non-interactive</c>, no agent, and stdin is a terminal.</summary>
    public bool IsInteractive { get; init; }

    public bool AssumeYes { get; init; }

    public TimeSpan? Timeout { get; init; }

    public bool NoWait { get; init; }

    /// <summary>Gets the refresh token from <c>VELIXO_TOKEN</c>, which replaces the stored one for this run.</summary>
    public string? EnvironmentToken { get; init; }

    /// <summary>Gets the OAuth client id from <c>VELIXO_CLIENT_ID</c>, for an <see cref="EnvironmentToken"/> on a machine that never signed in.</summary>
    public string? EnvironmentClientId { get; init; }

    /// <summary>Gets the display name of the target: the environment name, or the URL.</summary>
    public string Target => EnvironmentName ?? McpUrl.ToString();
}
