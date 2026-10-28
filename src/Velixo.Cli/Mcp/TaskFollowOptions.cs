namespace Velixo.Cli.Mcp;

/// <summary>How a command waits for a task: not at all with <c>--no-wait</c>, and for at most <see cref="Timeout"/>.</summary>
public sealed record TaskFollowOptions
{
    public bool NoWait { get; init; }

    /// <summary>Gets how long this wait may last: what is left of the command's <c>--timeout</c>, or null for no limit.</summary>
    public TimeSpan? Timeout { get; init; }
}
