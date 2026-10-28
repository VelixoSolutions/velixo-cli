namespace Velixo.Cli.Screen;

/// <summary>The flags of <c>velixo screen run</c>, as the command line gave them.</summary>
internal sealed record ScreenRunOptions
{
    required public string BatchPath { get; init; }

    public IReadOnlyList<string> Variables { get; init; } = [];

    public IReadOnlyList<string> NullVariables { get; init; } = [];

    public string? VariablesFile { get; init; }

    public string? RowsFile { get; init; }

    public IReadOnlyList<string> Attachments { get; init; } = [];

    public bool LeaveOpen { get; init; }

    public bool Continue { get; init; }

    public string? ContinuationFile { get; init; }

    public bool Finish { get; init; }

    public string? DownloadDirectory { get; init; }

    public bool DryRun { get; init; }

    public bool Force { get; init; }
}
