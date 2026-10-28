using Velixo.Cli.Config;

namespace Velixo.Cli.Mcp;

/// <summary>How a command waits for a task: not at all with <c>--no-wait</c>, and for at most <c>--timeout</c>.</summary>
public sealed record TaskFollowOptions
{
    public bool NoWait { get; init; }

    public TimeSpan? Timeout { get; init; }

    public static TaskFollowOptions From(ResolvedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new TaskFollowOptions { NoWait = settings.NoWait, Timeout = settings.Timeout };
    }
}
