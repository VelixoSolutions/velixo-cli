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

    /// <summary>
    /// Gets a value indicating whether a table may carry terminal hyperlinks (OSC 8): stdout is a terminal, no AI agent
    /// is detected, the terminal is not <c>TERM=dumb</c> nor a legacy Windows console, and not <c>--no-links</c>.
    /// </summary>
    public bool Hyperlinks { get; init; }

    /// <summary>Gets <c>--links</c> as true, <c>--no-links</c> as false, and neither as null.</summary>
    public bool? RecordLinks { get; init; }

    /// <summary>
    /// Gets a value indicating whether a query asks the server for the record its cells open: with <c>--links</c>, or
    /// for a table that can show them as hyperlinks, unless <c>--no-links</c>. An agent gets JSON, so it gets links only
    /// when it asks.
    /// </summary>
    public bool WantsRecordLinks => RecordLinks ?? (Format == OutputFormat.Table && Hyperlinks);

    /// <summary>Gets a value indicating whether a saved record's link prints under a table: always, unless <c>--no-links</c>.</summary>
    public bool ShowsRecordUrl => RecordLinks != false;
}
