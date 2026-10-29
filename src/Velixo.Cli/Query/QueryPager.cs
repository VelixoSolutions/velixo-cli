using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Output;

namespace Velixo.Cli.Query;

/// <summary>
/// Shows a query result at a terminal in a full-screen viewer, as <c>less</c> does: the column headers
/// stay at the top and the rows scroll under them. ↑ and ↓ move one row, PgUp, PgDn and Space one
/// screen, Home and End go to the first and the last row, ← and → scroll a table wider than the
/// terminal, and Esc or q quits. Rows are fetched one window of <c>--top</c> at a time as they scroll
/// into view, and a window already fetched is not fetched again. On quit, the rows that were on screen
/// print to the terminal, so they stay in its scrollback.
/// </summary>
internal static class QueryPager
{
    /// <summary>
    /// Gets whether a result is paged: a table on a terminal a person reads, with stdin to press keys on,
    /// for a single window rather than <c>--all</c>.
    /// </summary>
    public static bool Applies(CliServices services, QueryRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        return services.Output.Format == OutputFormat.Table
            && !options.All
            && !services.Settings.NoWait
            && !services.Console.IsOutputRedirected
            && services.Console.WindowHeight > 0
            && ValuePrompt.CanAsk(services);
    }

    /// <summary>
    /// Shows <paramref name="first"/>, the window that starts after <paramref name="skip"/> rows. A result
    /// whose every row fits on the screen prints as a table; any other opens the viewer. <paramref name="fetch"/>
    /// runs the same query for the window that starts after the rows it is given.
    /// </summary>
    public static async Task PageAsync(
        CliServices services,
        JsonObject first,
        int skip,
        int top,
        string? title,
        Func<int, CancellationToken, Task<JsonObject>> fetch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(fetch);

        TabularView? view = QueryOutput.IsQueryResult(first) ? TabularView.From(first) : null;
        var total = QueryOutput.ReadCount(first["result"]?["data"]?["totalCount"]);
        if (view is null || total is null || view.Rows.Count == 0 || FitsOnScreen(services, view.Rows.Count, skip, total.Value, title))
        {
            WriteTitle(services, title);
            QueryOutput.WriteResult(services.Output, first, skip);
            return;
        }

        var viewer = new Viewer(services, first["result"]!["data"]!["columns"] as JsonArray, view.Columns, skip, top, total.Value, title, fetch);
        viewer.Add(skip, view.Rows);
        await viewer.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns whether the whole result is in this window and prints without scrolling the headers off the screen.</summary>
    private static bool FitsOnScreen(CliServices services, int rows, int skip, long total, string? title) =>
        skip == 0 && rows >= total && (title is null ? 0 : 1) + 2 + rows + 1 < services.Console.WindowHeight;

    private static void WriteTitle(CliServices services, string? title)
    {
        if (title is not null)
        {
            services.Output.WriteText(services.Settings.Output.UseColor ? $"\u001b[1m{title}\u001b[0m" : title);
        }
    }

    private static string Grouped(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private sealed class Viewer(
        CliServices services,
        JsonArray? columns,
        IReadOnlyList<string> captions,
        int skip,
        int top,
        long total,
        string? title,
        Func<int, CancellationToken, Task<JsonObject>> fetch)
    {
        // The alternate screen keeps the viewer out of the scrollback, and alternate scroll (1007) makes
        // the mouse wheel send ↑ and ↓ there, where it would otherwise scroll nothing.
        private const string EnterScreen = "\u001b[?1049h\u001b[?25l\u001b[?1007h";
        private const string LeaveScreen = "\u001b[?1007l\u001b[?25h\u001b[?1049l";
        private const string Bold = "\u001b[1m";
        private const string Dim = "\u001b[2m";
        private const string Reset = "\u001b[0m";
        private const string ClearToEnd = "\u001b[K";

        private readonly ICliConsole _console = services.Console;
        private readonly bool _useColor = services.Settings.Output.UseColor;
        private readonly Dictionary<int, IReadOnlyList<string>> _rows = [];
        private readonly int _skip = skip;
        private readonly HashSet<int> _fetchedWindows = [skip];
        private readonly List<JsonObject> _fetched = [];
        private readonly Dictionary<int, string> _lines = [];
        private string[] _header = [];
        private int _widest;
        private bool _laidOut;
        private long _total = total;
        private int _firstRow = skip;
        private int _left;

        private int Width => _console.WindowWidth > 0 ? _console.WindowWidth : 80;

        /// <summary>Gets the lines left for rows once the title, the two header lines and the bar are drawn.</summary>
        private int BodyHeight => Math.Max(1, _console.WindowHeight - (title is null ? 0 : 1) - 2 - 1);

        private int LastFirstRow => (int)Math.Max(0, Math.Min(_total, int.MaxValue) - BodyHeight);

        public void Add(int start, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                _rows[start + index] = rows[index];
            }

            _laidOut = false;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            _console.Out.Write(EnterScreen);
            try
            {
                while (true)
                {
                    _firstRow = Math.Clamp(_firstRow, 0, LastFirstRow);
                    await LoadAsync(_firstRow, (int)Math.Min(_firstRow + BodyHeight, _total), cancellationToken).ConfigureAwait(false);
                    _firstRow = Math.Clamp(_firstRow, 0, LastFirstRow);
                    Draw();
                    if (!Move(_console.ReadKey()))
                    {
                        break;
                    }
                }
            }
            finally
            {
                _console.Out.Write(LeaveScreen);
                _console.Out.Flush();
            }

            PrintScreen();
            foreach (JsonObject document in _fetched)
            {
                QueryOutput.RelayWarnings(services.Output, document);
            }
        }

        /// <summary>Applies a key, and returns false when it quits.</summary>
        private bool Move(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.UpArrow or ConsoleKey.K:
                    _firstRow--;
                    break;
                case ConsoleKey.DownArrow or ConsoleKey.J:
                    _firstRow++;
                    break;
                case ConsoleKey.PageUp or ConsoleKey.B:
                    _firstRow -= BodyHeight;
                    break;
                case ConsoleKey.PageDown or ConsoleKey.Spacebar or ConsoleKey.F:
                    _firstRow += BodyHeight;
                    break;
                case ConsoleKey.Enter:
                    if (_firstRow >= LastFirstRow)
                    {
                        return false;
                    }

                    _firstRow += BodyHeight;
                    break;
                case ConsoleKey.Home:
                    _firstRow = 0;
                    break;
                case ConsoleKey.End:
                    _firstRow = LastFirstRow;
                    break;
                case ConsoleKey.LeftArrow or ConsoleKey.H:
                    _left = Math.Max(0, _left - SidewaysStep());
                    break;
                case ConsoleKey.RightArrow or ConsoleKey.L:
                    _left = Math.Min(Math.Max(0, _widest - Width), _left + SidewaysStep());
                    break;
                case ConsoleKey.Escape or ConsoleKey.Q:
                    return false;
                case ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                    return false;
            }

            return true;
        }

        private int SidewaysStep() => Math.Max(8, Width / 4);

        /// <summary>
        /// Fetches every window that holds a row from <paramref name="from"/> up to <paramref name="to"/>.
        /// A window that comes back without a row it should hold means the result shrank since the first
        /// call, so the result ends there.
        /// </summary>
        private async Task LoadAsync(int from, int to, CancellationToken cancellationToken)
        {
            for (var row = from; row < to && row < _total; row++)
            {
                if (_rows.ContainsKey(row))
                {
                    continue;
                }

                var start = WindowStart(row);
                if (_fetchedWindows.Add(start))
                {
                    DrawBar($"loading rows {Grouped(start + 1L)}–{Grouped(Math.Min(start + (long)top, _total))}…", keys: []);
                    JsonObject document = await fetch(start, cancellationToken).ConfigureAwait(false);
                    _fetched.Add(document);
                    if (QueryOutput.ReadCount(document["result"]?["data"]?["totalCount"]) is { } count)
                    {
                        _total = count;
                    }

                    if (TabularView.From(document) is { } page)
                    {
                        Add(start, page.Rows);
                    }
                }

                if (!_rows.ContainsKey(row))
                {
                    _total = Math.Min(_total, row);
                }
            }
        }

        /// <summary>Returns where the window that holds <paramref name="row"/> starts: windows of <c>--top</c> rows, lined up on <c>--skip</c>.</summary>
        private int WindowStart(int row)
        {
            if (row >= _skip)
            {
                return _skip + ((row - _skip) / top * top);
            }

            var windowsBefore = (_skip - row + top - 1) / top;
            return Math.Max(0, _skip - (windowsBefore * top));
        }

        /// <summary>Lays out every row fetched so far as one table, so the columns keep their widths as the rows scroll.</summary>
        private void LayOut()
        {
            if (_laidOut)
            {
                return;
            }

            var order = _rows.Keys.Order().ToList();
            (IReadOnlyList<IReadOnlyList<string>> cells, IReadOnlyList<bool> rightAligned) =
                QueryOutput.ForReading(columns, order.Select(row => _rows[row]).ToList());
            var lines = services.Output.FormatTable(captions, cells, rightAligned: rightAligned).Split('\n');

            _header = lines[..2];
            _lines.Clear();
            for (var index = 0; index < order.Count; index++)
            {
                _lines[order[index]] = lines[index + 2];
            }

            _widest = lines.Max(line => line.Length);
            _laidOut = true;
        }

        private void Draw()
        {
            LayOut();
            var frame = new StringBuilder("\u001b[H");
            if (title is not null)
            {
                AppendLine(frame, Cut(title, 0), Bold);
            }

            AppendLine(frame, Cut(_header[0], _left), Bold);
            AppendLine(frame, Cut(_header[1], _left), Dim);
            for (var row = _firstRow; row < _firstRow + BodyHeight; row++)
            {
                AppendLine(frame, _lines.TryGetValue(row, out var line) ? Cut(line, _left) : string.Empty, style: null);
            }

            _console.Out.Write(frame.ToString());
            var keys = new List<(string Key, string Action)> { ("↑↓", "scroll"), ("PgUp PgDn", "page"), ("Home End", "first, last") };
            if (_widest > Width)
            {
                keys.Add(("← →", "sideways"));
            }

            keys.Add(("Esc", "quit"));
            var last = Math.Min(_firstRow + (long)BodyHeight, _total);
            DrawBar($"rows {Grouped(_firstRow + 1L)}–{Grouped(last)} of {Grouped(_total)}", keys);
        }

        /// <summary>
        /// Draws the bar on the bottom line: the rows on screen in reverse video, then each key that fits,
        /// the key in bold cyan. Without colour, the same words, the rows in brackets.
        /// </summary>
        private void DrawBar(string label, IReadOnlyList<(string Key, string Action)> keys)
        {
            var room = Width - 1;
            var plain = new StringBuilder(_useColor ? $" {label} " : $"[ {label} ]");
            var styled = new StringBuilder(_useColor ? $"\u001b[7;1m {label} {Reset}" : plain.ToString());
            var separator = "   ";
            foreach ((var key, var action) in keys)
            {
                if (plain.Length + separator.Length + key.Length + 1 + action.Length > room)
                {
                    break;
                }

                plain.Append(separator).Append(key).Append(' ').Append(action);
                styled.Append(separator).Append(_useColor ? $"\u001b[1;36m{key}{Reset} {action}" : $"{key} {action}");
                separator = "  ";
            }

            var text = plain.Length > room ? plain.ToString(0, room) : styled.ToString();
            _console.Out.Write($"\u001b[{_console.WindowHeight.ToString(CultureInfo.InvariantCulture)};1H{text}{ClearToEnd}");
            _console.Out.Flush();
        }

        /// <summary>The part of <paramref name="line"/> the screen shows from column <paramref name="left"/>.</summary>
        private string Cut(string line, int left) =>
            line.Length <= left ? string.Empty : line.Substring(left, Math.Min(Width, line.Length - left));

        private void AppendLine(StringBuilder frame, string text, string? style)
        {
            frame.Append(_useColor && style is not null && text.Length > 0 ? $"{style}{text}{Reset}" : text);

            // A line that fills the width leaves the cursor waiting to wrap, and erasing there would erase its last character.
            if (text.Length < Width)
            {
                frame.Append(ClearToEnd);
            }

            frame.Append("\r\n");
        }

        /// <summary>Prints the rows that were on screen as a table, with the footer that says how to get the rest.</summary>
        private void PrintScreen()
        {
            WriteTitle(services, title);
            var last = (int)Math.Min(_firstRow + (long)BodyHeight, _total);
            var shown = Enumerable.Range(_firstRow, Math.Max(0, last - _firstRow)).Where(_lines.ContainsKey).Select(row => _lines[row]);
            var footer = QueryOutput.Footer(new JsonObject { ["totalCount"] = _total }, last - _firstRow, _firstRow);
            services.Output.WriteText(string.Join('\n', [.. _header, .. shown, footer]));
        }
    }
}
