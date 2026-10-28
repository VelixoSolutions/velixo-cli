namespace Velixo.Cli.Config;

/// <summary>The values of the shared global options as parsed from one command line.</summary>
public sealed record CommandLineValues
{
    public string? Profile { get; init; }

    public string? Environment { get; init; }

    public string? Url { get; init; }

    public string? Connection { get; init; }

    public string? Intent { get; init; }

    public string? Output { get; init; }

    public bool Quiet { get; init; }

    public bool NoColor { get; init; }

    public bool NonInteractive { get; init; }

    public bool Yes { get; init; }

    public int? TimeoutSeconds { get; init; }

    public bool NoWait { get; init; }

    public bool Compact { get; init; }
}
