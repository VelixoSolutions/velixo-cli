using System.Text;

namespace Velixo.Cli.Output;

/// <summary>
/// Lays out columns and rows as a plain-text table: a header of captions, a rule, and one line per
/// row, each column padded to its widest cell, on the left or, for a column of numbers, on the right.
/// </summary>
public static class TableFormatter
{
    private const string ColumnGap = "  ";
    private const char Ellipsis = '…';

    /// <param name="maxCellWidth">
    /// The widest a cell may print before it is cut with an ellipsis; null prints every cell whole,
    /// which is what a table written to a file gets.
    /// </param>
    /// <param name="rightAligned">For each column, whether it lines up on the right, as numbers do; null lines up every column on the left.</param>
    /// <param name="links">
    /// For each row, the URL each cell opens, written as a terminal hyperlink around the cell's text, so it takes no
    /// room; null links nothing.
    /// </param>
    /// <param name="uncut">For each column, whether its cells always print whole, as a URL must; null cuts every column alike.</param>
    public static string Format(
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string?>> rows,
        int? maxCellWidth = null,
        string? footer = null,
        IReadOnlyList<bool>? rightAligned = null,
        IReadOnlyList<IReadOnlyList<string?>>? links = null,
        IReadOnlyList<bool>? uncut = null)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        var lines = rows
            .Select(row => columns.Select((_, index) => Fit(index < row.Count ? row[index] : null, uncut is not null && index < uncut.Count && uncut[index] ? null : maxCellWidth)).ToArray())
            .ToList();
        var header = columns.Select(column => Fit(column, maxCellWidth)).ToArray();

        var widths = header.Select(caption => caption.Length).ToArray();
        foreach (var line in lines)
        {
            for (var index = 0; index < widths.Length; index++)
            {
                widths[index] = Math.Max(widths[index], line[index].Length);
            }
        }

        var right = columns.Select((_, index) => rightAligned is not null && index < rightAligned.Count && rightAligned[index]).ToArray();
        var text = new StringBuilder();
        AppendLine(text, header, widths, right, links: null);
        AppendLine(text, widths.Select(width => new string('-', width)).ToArray(), widths, right, links: null);
        for (var row = 0; row < lines.Count; row++)
        {
            AppendLine(text, lines[row], widths, right, links is not null && row < links.Count ? links[row] : null);
        }

        if (!string.IsNullOrEmpty(footer))
        {
            text.Append(footer).Append('\n');
        }

        return text.ToString();
    }

    private static void AppendLine(StringBuilder text, string[] cells, int[] widths, bool[] right, IReadOnlyList<string?>? links)
    {
        var line = new StringBuilder();
        for (var index = 0; index < cells.Length; index++)
        {
            if (index > 0)
            {
                line.Append(ColumnGap);
            }

            var padding = new string(' ', widths[index] - cells[index].Length);
            var cell = Hyperlink.Wrap(cells[index], links is not null && index < links.Count ? links[index] : null);
            line.Append(right[index] ? padding + cell : cell + padding);
        }

        text.Append(line.ToString().TrimEnd()).Append('\n');
    }

    private static string Fit(string? cell, int? maxCellWidth)
    {
        var flat = (cell ?? string.Empty).Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');

        return maxCellWidth is { } max && max > 1 && flat.Length > max
            ? string.Concat(flat.AsSpan(0, max - 1), Ellipsis.ToString())
            : flat;
    }
}
