namespace Velixo.Cli.Output;

public sealed record OutputSettings
{
    public OutputFormat Format { get; init; } = OutputFormat.Json;

    /// <summary>Gets a value indicating whether the format came from <c>--output</c>, <c>VELIXO_OUTPUT</c> or the config, rather than from where stdout goes.</summary>
    public bool IsFormatExplicit { get; init; }

    public bool Quiet { get; init; }

    public bool Compact { get; init; }

    public bool UseColor { get; init; }

    /// <summary>
    /// Gets a value indicating whether a wait shows as a spinner on stderr: stderr is a terminal, the run is
    /// interactive and not <c>--quiet</c>, and the terminal is not <c>TERM=dumb</c>.
    /// </summary>
    public bool Animate { get; init; }
}
