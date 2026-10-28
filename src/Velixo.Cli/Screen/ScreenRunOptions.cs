namespace Velixo.Cli.Screen;

/// <summary>The flags of <c>velixo screen run</c>, as the command line gave them.</summary>
internal sealed record ScreenRunOptions
{
    required public string BatchPath { get; init; }

    public IReadOnlyList<string> Variables { get; init; } = [];

    public IReadOnlyList<string> NullVariables { get; init; } = [];

    public string? VariablesFile { get; init; }

    public string? RowsFile { get; init; }

    public IReadOnlyList<ScreenAttachment> Attachments { get; init; } = [];

    public bool LeaveOpen { get; init; }

    public bool Continue { get; init; }

    public string? ContinuationFile { get; init; }

    public bool Finish { get; init; }

    public string? DownloadDirectory { get; init; }

    public bool DryRun { get; init; }

    public bool Force { get; init; }
}

/// <summary>One <c>--attach</c>: a local file that exists, and the comment stored beside it on the record.</summary>
internal sealed record ScreenAttachment(string Path, string? Comment)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public long SizeBytes => new FileInfo(Path).Length;
}
