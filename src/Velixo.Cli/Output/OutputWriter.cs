using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Velixo.Cli.Output;

/// <summary>
/// Everything a command prints. stdout carries the one result document in the chosen format;
/// progress, warnings and errors go to stderr, so a pipe only ever sees data.
/// </summary>
public sealed class OutputWriter(ICliConsole console, OutputSettings settings, TimeProvider? clock = null)
{
    private const string Red = "\u001b[31m";
    private const string Yellow = "\u001b[33m";
    private const string Reset = "\u001b[0m";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private Activity? _activity;
    private int _activityHolds;

    public OutputSettings Settings { get; } = settings;

    /// <summary>Gets the lock that keeps a spinner's frames and the lines printed on stderr from crossing.</summary>
    internal Lock Sync { get; } = new();

    /// <summary>Gets or sets the frames a spinner draws; the console's encoding decides, and a test can set them.</summary>
    internal string[] SpinnerFrames { get; set; } = CanShowBraille() ? Activity.BrailleFrames : Activity.AsciiFrames;

    /// <summary>
    /// Begins something the CLI waits on. At a terminal it is a spinner showing <paramref name="label"/>, which appears
    /// only once the wait has lasted a moment; anywhere else <paramref name="label"/> prints as a progress line when
    /// <paramref name="announce"/>, and each later <see cref="Activity.Update"/> prints too. An activity begun inside
    /// another shows nothing, so the outer one keeps its label.
    /// </summary>
    public Activity BeginActivity(string label, bool announce = false)
    {
        ArgumentNullException.ThrowIfNull(label);

        lock (Sync)
        {
            if (_activity is not null || _activityHolds > 0 || Settings.Quiet)
            {
                return new Activity(this, label, silent: true);
            }

            if (!Settings.Animate)
            {
                if (announce)
                {
                    console.Error.WriteLine(label);
                }

                return new Activity(this, label, silent: false);
            }

            _activity = new Activity(this, label, console.Error, _clock, Settings.UseColor, SpinnerFrames, () => console.WindowWidth);
            return _activity;
        }
    }

    /// <summary>
    /// Keeps spinners off the screen until the returned scope is disposed, while a full-screen view such as the query
    /// viewer owns the terminal and draws its own loading line.
    /// </summary>
    public IDisposable HoldActivities()
    {
        lock (Sync)
        {
            _activityHolds++;
            _activity?.EraseLocked();
        }

        return new Hold(this);
    }

    /// <summary>Gets whether a full-screen view holds spinners off the screen; read it under <see cref="Sync"/>.</summary>
    internal bool ActivitiesHeld => _activityHolds > 0;

    internal void End(Activity activity)
    {
        if (ReferenceEquals(_activity, activity))
        {
            _activity = null;
        }
    }

    public OutputFormat Format => Settings.Format;

    /// <summary>Writes <paramref name="document"/> as JSON, indented unless <c>--compact</c>.</summary>
    public void WriteJson(JsonNode? document)
    {
        console.Out.Write(Settings.Compact ? JsonText.Compact(document) : JsonText.Indented(document));
        console.Out.Write('\n');
    }

    public void WriteJson<T>(T value, JsonTypeInfo<T> typeInfo) =>
        WriteJson(JsonSerializer.SerializeToNode(value, typeInfo));

    public void WriteTable(
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string?>> rows,
        string? footer = null,
        IReadOnlyList<bool>? rightAligned = null,
        IReadOnlyList<IReadOnlyList<string?>>? links = null) =>
        console.Out.Write(FormatTable(columns, rows, footer, rightAligned, links));

    /// <summary>
    /// Lays out a table as <see cref="WriteTable"/> prints it, cutting long cells to fit the terminal. A cell with a
    /// link in <paramref name="links"/> is a terminal hyperlink where <see cref="OutputSettings.Hyperlinks"/> allows
    /// one; anywhere else its URL goes in a column of its own.
    /// </summary>
    public string FormatTable(
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string?>> rows,
        string? footer = null,
        IReadOnlyList<bool>? rightAligned = null,
        IReadOnlyList<IReadOnlyList<string?>>? links = null)
    {
        int? maxCellWidth = console.IsOutputRedirected || console.WindowWidth <= 0
            ? null
            : Math.Max(20, console.WindowWidth / 2);
        if (links is not null && !Settings.Hyperlinks)
        {
            (columns, IReadOnlyList<IReadOnlyList<string?>> withUrls, rightAligned) = RecordLinks.AddUrlColumns(columns, rows, links, rightAligned);

            // A URL is never cut: a cut one opens nothing.
            return TableFormatter.Format(columns, withUrls, maxCellWidth: null, footer, rightAligned);
        }

        return TableFormatter.Format(columns, rows, maxCellWidth, footer, rightAligned, links);
    }

    /// <summary>Writes CSV; a cell with a link in <paramref name="links"/> gets its URL in a column of its own beside it.</summary>
    public void WriteCsv(
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string?>> rows,
        IReadOnlyList<IReadOnlyList<string?>>? links = null)
    {
        if (links is not null)
        {
            (columns, IReadOnlyList<IReadOnlyList<string?>> withUrls, _) = RecordLinks.AddUrlColumns(columns, rows, links);
            rows = withUrls;
        }

        console.Out.Write(CsvFormatter.Format(columns, rows));
    }

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
            WriteCsv(view.Columns, view.Rows, view.Links);
        }
        else
        {
            WriteTable(view.Columns, view.Rows, view.Footer, links: view.Links);
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
            WriteError(line);
        }
    }

    /// <summary>Writes a warning to stderr, even with <c>--quiet</c>: a warning, such as a parameter that matched nothing, is never noise.</summary>
    public void Warn(string message) =>
        WriteError(Settings.UseColor ? $"{Yellow}warning:{Reset} {message}" : $"warning: {message}");

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
        WriteError($"{prefix} {exception.Message} [{exception.Code}]");
        if (!string.IsNullOrEmpty(exception.Detail))
        {
            WriteError($"  detail: {exception.Detail}");
        }

        if (!string.IsNullOrEmpty(exception.TaskId))
        {
            WriteError($"  task: {exception.TaskId}");
        }

        if (!string.IsNullOrEmpty(exception.NextStep))
        {
            WriteError($"  next: {exception.NextStep}");
        }
    }

    /// <summary>Writes a line to stderr above a spinner that is showing, which then draws again below it.</summary>
    private void WriteError(string line)
    {
        lock (Sync)
        {
            _activity?.EraseLocked();
            console.Error.WriteLine(line);
            _activity?.RedrawLocked();
        }
    }

    /// <summary>
    /// Tells whether the console can show the braille frames: any terminal but a Windows console on a legacy code
    /// page, which shows them as question marks.
    /// </summary>
    private static bool CanShowBraille()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            return Console.OutputEncoding.CodePage == 65001;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private sealed class Hold(OutputWriter owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            lock (owner.Sync)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    owner._activityHolds--;
                }
            }
        }
    }
}
