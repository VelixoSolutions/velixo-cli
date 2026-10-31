using System.Text;

namespace Velixo.Cli.Output;

/// <summary>
/// Writes RFC 4180 CSV: the captions as the header row, then one line per row. A field is quoted
/// when it holds a comma, a quote, a line break or leading or trailing space, and a quote inside
/// it is doubled. Lines end in <c>\n</c>, which every CSV reader accepts.
/// </summary>
public static class CsvFormatter
{
    public static string Format(IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder();
        AppendRecord(text, columns);
        foreach (var row in rows)
        {
            AppendRecord(text, columns.Select((_, index) => index < row.Count ? row[index] : null).ToList());
        }

        return text.ToString();
    }

    public static string Escape(string? field)
    {
        var value = field ?? string.Empty;
        var needsQuotes = value.Length > 0
            && (value.AsSpan().IndexOfAny(",\"\r\n") >= 0 || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]));

        return needsQuotes ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    private static void AppendRecord(StringBuilder text, IReadOnlyList<string?> fields)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            if (index > 0)
            {
                text.Append(',');
            }

            text.Append(Escape(fields[index]));
        }

        text.Append('\n');
    }
}
