using System.Text.Json.Nodes;

namespace Velixo.Cli.Screen;

/// <summary>The flags of <c>velixo command run</c>, as the command line gave them.</summary>
internal sealed record ScreenRunOptions
{
    /// <summary>Gets the batch file, or <c>-</c> for stdin; null when the batch comes from the library.</summary>
    public string? BatchPath { get; init; }

    /// <summary>Gets the library entry's batch, when the run names one by its number instead of a file.</summary>
    public JsonObject? BatchDocument { get; init; }

    /// <summary>Gets where the batch came from, for messages: a file, stdin, or a library number.</summary>
    public string BatchSource { get; init; } = "the batch";

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

/// <summary>
/// One file to attach, and the comment stored beside it on the record: an <c>--attach</c>, a local file
/// the run stages itself, or an <c>--attach-token</c>, a file <c>velixo files stage</c> already staged.
/// </summary>
internal sealed record ScreenAttachment(string Path, string? Comment)
{
    /// <summary>Gets the token of a file already staged, or null for a local file the run stages.</summary>
    public string? FileToken { get; init; }

    public string? StagedName { get; init; }

    public long StagedSize { get; init; }

    public string FileName => StagedName ?? System.IO.Path.GetFileName(Path);

    public long SizeBytes => FileToken is null ? new FileInfo(Path).Length : StagedSize;
}
