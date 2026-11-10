using System.Text;
using System.Text.Json.Nodes;

namespace Velixo.Cli.Output;

/// <summary>
/// Reads the <c>recordLinks</c> that <c>execute_query</c> returns with <c>includeRecordLinks</c> into a URL
/// per cell, and puts them in columns of their own where a link cannot be clicked.
/// </summary>
/// <remarks>
/// <c>recordLinks.rows[i][j]</c> says whether the cell <c>rows[i][columns[j].column]</c> opens a record. On a
/// column with <c>keys</c>, <c>1</c> opens it on the column's <c>screenId</c> and a string on that screen ID;
/// the link is <c>baseUrl</c> + <c>ScreenId=</c> + screen, then <c>&amp;name=value</c> for each key in order.
/// On a column with <c>rest: true</c>, the string is the rest of the link after <c>baseUrl</c>. Anything else links nothing.
/// </remarks>
public static class RecordLinks
{
    /// <summary>The suffix of the caption of a column of URLs added beside a linked column.</summary>
    public const string ColumnSuffix = " URL";

    /// <summary>
    /// The link of each cell of <paramref name="rows"/>, null where a cell links nothing; or null when
    /// <paramref name="recordLinks"/> links no cell at all.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string?>>? Resolve(JsonNode? recordLinks, JsonArray rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (recordLinks is not JsonObject links
            || TabularView.CellText(links["baseUrl"]) is not { Length: > 0 } baseUrl
            || links["columns"] is not JsonArray columns
            || links["rows"] is not JsonArray flags)
        {
            return null;
        }

        var any = false;
        var result = new List<IReadOnlyList<string?>>(rows.Count);
        for (var row = 0; row < rows.Count; row++)
        {
            var values = rows[row] as JsonArray ?? [];
            var cells = new string?[values.Count];
            if (row < flags.Count && flags[row] is JsonArray rowFlags)
            {
                for (var entry = 0; entry < columns.Count && entry < rowFlags.Count; entry++)
                {
                    if (columns[entry] is JsonObject column
                        && ReadIndex(column["column"]) is { } cell
                        && cell < cells.Length
                        && Link(baseUrl, column, rowFlags[entry], values) is { } url
                        && Hyperlink.IsSafe(url))
                    {
                        cells[cell] = url;
                        any = true;
                    }
                }
            }

            result.Add(cells);
        }

        return any ? result : null;
    }

    /// <summary>
    /// Adds a column of URLs right after each column that links a record, for output where a link cannot be
    /// clicked, such as CSV. The new column takes the caption of its column with <see cref="ColumnSuffix"/>.
    /// </summary>
    public static (IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows, IReadOnlyList<bool>? RightAligned) AddUrlColumns(
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string?>> rows,
        IReadOnlyList<IReadOnlyList<string?>> links,
        IReadOnlyList<bool>? rightAligned = null)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(links);

        var linked = Enumerable.Range(0, columns.Count)
            .Select(index => links.Any(row => index < row.Count && row[index] is not null))
            .ToArray();

        var captions = new List<string>();
        var aligned = new List<bool>();
        for (var index = 0; index < columns.Count; index++)
        {
            captions.Add(columns[index]);
            aligned.Add(rightAligned is not null && index < rightAligned.Count && rightAligned[index]);
            if (linked[index])
            {
                captions.Add(columns[index] + ColumnSuffix);
                aligned.Add(false);
            }
        }

        var expanded = rows
            .Select((row, rowIndex) =>
            {
                var cells = new List<string?>();
                for (var index = 0; index < columns.Count; index++)
                {
                    cells.Add(index < row.Count ? row[index] : null);
                    if (linked[index])
                    {
                        cells.Add(rowIndex < links.Count && index < links[rowIndex].Count ? links[rowIndex][index] : null);
                    }
                }

                return (IReadOnlyList<string?>)cells;
            })
            .ToList();

        return (captions, expanded, rightAligned is null ? null : aligned);
    }

    private static string? Link(string baseUrl, JsonObject column, JsonNode? flag, JsonArray values)
    {
        if (column["rest"] is JsonValue rest && rest.TryGetValue(out bool isRest) && isRest)
        {
            return flag is JsonValue tail && tail.TryGetValue(out string? path) && !string.IsNullOrEmpty(path) ? baseUrl + path : null;
        }

        if (column["keys"] is not JsonObject keys)
        {
            return null;
        }

        string? screen = flag switch
        {
            JsonValue value when value.TryGetValue(out string? named) => named,
            JsonValue value when ReadIndex(value) == 1 => TabularView.CellText(column["screenId"]),
            _ => null,
        };
        if (string.IsNullOrEmpty(screen))
        {
            return null;
        }

        var url = new StringBuilder(baseUrl).Append("ScreenId=").Append(Uri.EscapeDataString(screen));
        foreach ((var name, JsonNode? keyColumn) in keys)
        {
            if (ReadIndex(keyColumn) is not { } source || source >= values.Count)
            {
                return null;
            }

            var value = TabularView.CellText(values[source]).TrimEnd(' ');
            url.Append('&').Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return url.ToString();
    }

    private static int? ReadIndex(JsonNode? node) =>
        node is JsonValue value && int.TryParse(value.ToJsonString(), out var index) && index >= 0 ? index : null;
}
