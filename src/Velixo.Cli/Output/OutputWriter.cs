using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Velixo.Cli.Output;

/// <summary>
/// Everything a command prints. stdout carries the one result document in the chosen format;
/// progress, warnings and errors go to stderr, so a pipe only ever sees data.
/// </summary>
public sealed class OutputWriter(ICliConsole console, OutputSettings settings)
{
    private const string Red = "\u001b[31m";
    private const string Yellow = "\u001b[33m";
    private const string Reset = "\u001b[0m";

    public OutputSettings Settings { get; } = settings;

    public OutputFormat Format => Settings.Format;

    /// <summary>Writes <paramref name="document"/> as JSON, indented unless <c>--compact</c>.</summary>
    public void WriteJson(JsonNode? document)
    {
        console.Out.Write(Settings.Compact ? JsonText.Compact(document) : JsonText.Indented(document));
        console.Out.Write('\n');
    }

    public void WriteJson<T>(T value, JsonTypeInfo<T> typeInfo) =>
        WriteJson(JsonSerializer.SerializeToNode(value, typeInfo));

    public void WriteTable(IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<string?>> rows, string? footer = null)
    {
        int? maxCellWidth = console.IsOutputRedirected || console.WindowWidth <= 0
            ? null
            : Math.Max(20, console.WindowWidth / 2);
        console.Out.Write(TableFormatter.Format(columns, rows, maxCellWidth, footer));
    }

    public void WriteCsv(IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<string?>> rows) =>
        console.Out.Write(CsvFormatter.Format(columns, rows));

    /// <summary>
    /// Writes a result document in the chosen format: JSON as it is, a table or CSV from its
    /// <see cref="TabularView"/>. A document with no tabular reading prints as JSON in table mode
    /// and is refused in CSV mode.
    /// </summary>
    public void WriteResult(JsonNode? document)
    {
        if (Format == OutputFormat.Json)
        {
            WriteJson(document);
            return;
        }

        TabularView? view = TabularView.From(document);
        if (view is null)
        {
            if (Format == OutputFormat.Csv)
            {
                throw CliException.Usage("This result has no rows to write as CSV.", "Use --output json.");
            }

            WriteText(TabularView.CellText(document));
            return;
        }

        if (Format == OutputFormat.Csv)
        {
            WriteCsv(view.Columns, view.Rows);
        }
        else
        {
            WriteTable(view.Columns, view.Rows, view.Footer);
        }
    }

    /// <summary>Writes plain text to stdout, ending it with a newline when it has none.</summary>
    public void WriteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        console.Out.Write(text);
        if (!text.EndsWith('\n'))
        {
            console.Out.Write('\n');
        }
    }

    /// <summary>Writes a progress line to stderr, unless <c>--quiet</c>.</summary>
    public void Progress(string line)
    {
        if (!Settings.Quiet)
        {
            console.Error.WriteLine(line);
        }
    }

    /// <summary>Writes a warning to stderr, unless <c>--quiet</c>.</summary>
    public void Warn(string message)
    {
        if (!Settings.Quiet)
        {
            console.Error.WriteLine(Settings.UseColor ? $"{Yellow}warning:{Reset} {message}" : $"warning: {message}");
        }
    }

    /// <summary>
    /// Reports a failure: the error envelope on stdout in JSON mode, text on stderr otherwise. Errors
    /// print even under <c>--quiet</c>.
    /// </summary>
    public void WriteError(CliException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (Format == OutputFormat.Json)
        {
            WriteJson(ErrorEnvelope.From(exception), Serialization.CliJsonContext.Default.ErrorEnvelope);
            return;
        }

        var prefix = Settings.UseColor ? $"{Red}error:{Reset}" : "error:";
        console.Error.WriteLine($"{prefix} {exception.Message} [{exception.Code}]");
        if (!string.IsNullOrEmpty(exception.Detail))
        {
            console.Error.WriteLine($"  detail: {exception.Detail}");
        }

        if (!string.IsNullOrEmpty(exception.TaskId))
        {
            console.Error.WriteLine($"  task: {exception.TaskId}");
        }

        if (!string.IsNullOrEmpty(exception.NextStep))
        {
            console.Error.WriteLine($"  next: {exception.NextStep}");
        }
    }
}
