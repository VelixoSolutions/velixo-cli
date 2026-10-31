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
    public static string Format(
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string?>> rows,
        int? maxCellWidth = null,
        string? footer = null,
        IReadOnlyList<bool>? rightAligned = null)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        var lines = rows
            .Select(row => columns.Select((_, index) => Fit(index < row.Count ? row[index] : null, maxCellWidth)).ToArray())
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
        AppendLine(text, header, widths, right);
        AppendLine(text, widths.Select(width => new string('-', width)).ToArray(), widths, right);
        foreach (var line in lines)
        {
            AppendLine(text, line, widths, right);
        }

        if (!string.IsNullOrEmpty(footer))
        {
            text.Append(footer).Append('\n');
        }

        return text.ToString();
    }

    private static void AppendLine(StringBuilder text, string[] cells, int[] widths, bool[] right)
    {
        var line = new StringBuilder();
        for (var index = 0; index < cells.Length; index++)
        {
            if (index > 0)
            {
                line.Append(ColumnGap);
            }

            line.Append(right[index] ? cells[index].PadLeft(widths[index]) : cells[index].PadRight(widths[index]));
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
