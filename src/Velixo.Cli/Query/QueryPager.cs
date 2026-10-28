using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Output;

namespace Velixo.Cli.Query;

/// <summary>
/// Pages through a query result at a terminal: Enter, → or Space shows the next page, ← the previous,
/// Home the first and End the last, Esc or q quits. Each page prints below the last, as <c>more</c>
/// does, so every page stays in the terminal's scrollback; a highlighted bar under it names the page
/// and the keys, and is erased when a key is pressed. A page already seen is not fetched again.
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
            && ValuePrompt.CanAsk(services);
    }

    /// <summary>
    /// Prints <paramref name="first"/>, the window that starts after <paramref name="skip"/> rows, and
    /// pages from there while the person asks for more. <paramref name="fetch"/> runs the same query
    /// for the window that starts after the rows it is given.
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

        var pages = new Dictionary<int, JsonObject> { [skip] = first };
        var current = skip;
        var useColor = services.Settings.Output.UseColor;

        while (true)
        {
            JsonObject document = pages[current];
            if (title is not null)
            {
                services.Output.WriteText(useColor ? $"\u001b[1m{title}\u001b[0m" : title);
            }

            QueryOutput.WriteResult(services.Output, document, current, paging: true);

            var total = QueryOutput.ReadCount(document["result"]?["data"]?["totalCount"]);
            var rows = (document["result"]?["data"]?["rows"] as JsonArray)?.Count ?? 0;
            if (total is null || (current == 0 && rows >= total))
            {
                return;
            }

            var last = (int)Math.Max(0, (total.Value - 1) / top * top);
            var next = await AskAsync(services, current, top, rows, total.Value, last, useColor).ConfigureAwait(false);
            if (next is not { } wanted)
            {
                return;
            }

            if (!pages.ContainsKey(wanted))
            {
                JsonObject fetched = await fetch(wanted, cancellationToken).ConfigureAwait(false);
                QueryOutput.RelayWarnings(services.Output, fetched);
                pages[wanted] = fetched;
            }

            current = wanted;
            services.Output.WriteText(string.Empty);
        }
    }

    /// <summary>Shows the bar and returns the skip of the page asked for, or null to quit.</summary>
    private static Task<int?> AskAsync(CliServices services, int current, int top, int rows, long total, int last, bool useColor)
    {
        var hasPrevious = current > 0;
        var hasNext = current + rows < total;
        TextWriter bar = services.Console.Error;

        while (true)
        {
            bar.Write(Bar(current, top, rows, total, hasPrevious, hasNext, useColor));
            bar.Flush();
            ConsoleKeyInfo key = services.Console.ReadKey();
            bar.Write("\r\u001b[2K");
            bar.Flush();

            switch (key.Key)
            {
                case ConsoleKey.Enter or ConsoleKey.Spacebar or ConsoleKey.RightArrow or ConsoleKey.PageDown or ConsoleKey.DownArrow:
                    if (hasNext)
                    {
                        return Task.FromResult<int?>(current + top);
                    }

                    if (key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar)
                    {
                        return Task.FromResult<int?>(null);
                    }

                    break;
                case ConsoleKey.LeftArrow or ConsoleKey.PageUp or ConsoleKey.UpArrow when hasPrevious:
                    return Task.FromResult<int?>(Math.Max(0, current - top));
                case ConsoleKey.Home when current != 0:
                    return Task.FromResult<int?>(0);
                case ConsoleKey.End when current != last:
                    return Task.FromResult<int?>(last);
                case ConsoleKey.Escape or ConsoleKey.Q:
                    return Task.FromResult<int?>(null);
                case ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                    return Task.FromResult<int?>(null);
            }
        }
    }

    /// <summary>
    /// The bar under a page: the page and the window in reverse video, then each key that does something
    /// on this page, the key in bold cyan. Without colour, the same words in brackets.
    /// </summary>
    private static string Bar(int current, int top, int rows, long total, bool hasPrevious, bool hasNext, bool useColor)
    {
        var page = (current / top) + 1;
        var pageCount = (int)((total + top - 1) / top);
        var window = rows == 0
            ? $"no rows after row {Grouped(current)} of {Grouped(total)}"
            : $"rows {Grouped(current + 1)}–{Grouped(current + rows)} of {Grouped(total)}";

        var keys = new List<(string Key, string Action)>();
        keys.Add(hasNext ? ("Enter →", "next") : ("Enter", "quit"));
        if (hasPrevious)
        {
            keys.Add(("←", "previous"));
        }

        if (current != 0)
        {
            keys.Add(("Home", "first"));
        }

        if (hasNext)
        {
            keys.Add(("End", "last"));
        }

        keys.Add(("Esc", "quit"));

        var text = new StringBuilder();
        if (useColor)
        {
            text.Append($"\u001b[7;1m Page {page} of {pageCount} \u001b[0m \u001b[2m{window}\u001b[0m   ");
            text.AppendJoin("  ", keys.Select(key => $"\u001b[1;36m{key.Key}\u001b[0m {key.Action}"));
        }
        else
        {
            text.Append($"[ Page {page} of {pageCount} · {window} ]   ");
            text.AppendJoin("  ", keys.Select(key => $"{key.Key} {key.Action}"));
        }

        return text.ToString();
    }

    private static string Grouped(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
