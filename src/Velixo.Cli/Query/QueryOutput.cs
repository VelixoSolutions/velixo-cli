using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Velixo.Cli.Commands;
using Velixo.Cli.Output;

namespace Velixo.Cli.Query;

/// <summary>
/// Prints what the query commands return. A query result prints its rows under their captions in
/// table and CSV mode, and its table ends with a footer naming the window; a plan prints its items.
/// Anything else prints the way every command prints a document.
/// </summary>
internal static partial class QueryOutput
{
    /// <summary>The file name the run command suggests when the plan was not saved.</summary>
    public const string SuggestedPlanFile = "plan.json";

    /// <summary>The file name suggested for the query text alone.</summary>
    public const string SuggestedQueryFile = "query.vql";

    /// <summary>Returns whether <paramref name="path"/> names a file of query text, which <c>--save</c> writes and <c>--query</c> reads.</summary>
    public static bool IsQueryFile(string path) => path.EndsWith(".vql", StringComparison.OrdinalIgnoreCase);

    /// <summary>How the server's warning for a partial window starts.</summary>
    private const string PartialWarningStart = "WARNING: this result is PARTIAL.";

    private static readonly string[] _fractionalTypes = ["decimal", "double", "float", "single"];

    private static readonly string[] _wholeTypes = ["int16", "int32", "int64", "int", "long", "short", "byte", "integer"];

    /// <summary>
    /// Repeats on stderr what a query result says about itself: a partial window, the ERP's warnings,
    /// and the warnings the tool sent beside its result. A table leaves out the server's partial-result
    /// warning, which is written for agents: the table's footer names the window and how to get the rest.
    /// </summary>
    public static void RelayWarnings(OutputWriter output, JsonNode? document)
    {
        ArgumentNullException.ThrowIfNull(output);

        CommandText.RelayWarnings(output, document);
        if (document?["result"]?["data"] is not JsonObject data)
        {
            return;
        }

        if (CommandText.String(data["warning"]) is { Length: > 0 } warning)
        {
            if (output.Format != OutputFormat.Table || !warning.StartsWith(PartialWarningStart, StringComparison.Ordinal))
            {
                output.Warn(warning);
            }
        }
        else if (output.Format != OutputFormat.Table && data["truncated"] is JsonValue truncated && truncated.TryGetValue(out bool isTruncated) && isTruncated)
        {
            output.Warn("The result is partial: the ERP matched more rows than this call returned.");
        }

        if (data["warnings"] is JsonArray warnings)
        {
            foreach (JsonNode? item in warnings)
            {
                output.Warn(CommandText.String(item) ?? item?.ToJsonString() ?? string.Empty);
            }
        }
    }

    /// <summary>Prints one query result whose window starts after <paramref name="skip"/> rows.</summary>
    /// <param name="paging">Whether the pager prints the result: the footer then names the window without the flags for the rest, which the pager's keys replace.</param>
    public static void WriteResult(OutputWriter output, JsonObject document, int skip, bool paging = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);

        if (output.Format == OutputFormat.Json || !IsQueryResult(document))
        {
            output.WriteResult(document);
            return;
        }

        TabularView view = TabularView.From(document)!;
        if (output.Format == OutputFormat.Csv)
        {
            output.WriteCsv(view.Columns, view.Rows);
        }
        else
        {
            JsonObject data = (JsonObject)document["result"]!["data"]!;
            (IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyList<bool> rightAligned) = ForReading(data["columns"] as JsonArray, view.Rows);
            output.WriteTable(view.Columns, rows, Footer(data, view.Rows.Count, skip, paging), rightAligned);
        }
    }

    /// <summary>
    /// The cells of a query result as a person reads them in a table. An amount gets thousands separators
    /// and the fewest decimals that show every value of its column: at least 2, at most the column's
    /// precision, so the decimal points line up. A whole number stays as it is, since it is often an ID.
    /// Both line up on the right. CSV and JSON keep the values as the ERP sent them.
    /// </summary>
    public static (IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<bool> RightAligned) ForReading(
        JsonArray? columns,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var count = rows.Count == 0 ? columns?.Count ?? 0 : rows.Max(row => row.Count);
        var rightAligned = new bool[count];
        var cells = rows.Select(row => row.ToArray()).ToList();

        for (var index = 0; index < count; index++)
        {
            var type = CommandText.String(columns?.ElementAtOrDefault(index)?["type"])?.ToLowerInvariant();
            var values = cells.Select(row => index < row.Length ? row[index] : string.Empty).ToList();
            if (type is null || !values.All(value => value.Length == 0 || decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                continue;
            }

            if (_wholeTypes.Contains(type))
            {
                rightAligned[index] = true;
            }
            else if (_fractionalTypes.Contains(type))
            {
                rightAligned[index] = true;
                var precision = ReadCount(columns![index]?["precision"]) is { } declared ? (int)Math.Clamp(declared, 0, 10) : 10;
                var parsed = values.Select(value => value.Length == 0 ? (decimal?)null : decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToList();
                var needed = parsed.Where(value => value is not null).Select(value => Scale(value!.Value)).DefaultIfEmpty(0).Max();
                var decimals = Math.Clamp(needed, Math.Min(2, precision), precision);
                for (var row = 0; row < cells.Count; row++)
                {
                    if (parsed[row] is { } value && index < cells[row].Length)
                    {
                        cells[row][index] = value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        return (cells, rightAligned);
    }

    /// <summary>The decimals <paramref name="value"/> needs once trailing zeros are dropped.</summary>
    private static int Scale(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        var point = text.IndexOf('.', StringComparison.Ordinal);
        return point < 0 ? 0 : text.TrimEnd('0').Length - point - 1;
    }

    /// <summary>Prints the result of <c>query run --plan</c>: the plan's note, then each item's result in order.</summary>
    public static void WritePlanResults(OutputWriter output, JsonObject combined, int skip)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(combined);

        if (output.Format == OutputFormat.Json)
        {
            output.WriteJson(combined);
            return;
        }

        // The plan's note is left out of a table: it was shown when the plan was built, and it is in the JSON.
        var results = combined["results"] as JsonArray ?? [];

        foreach (JsonObject entry in results.OfType<JsonObject>())
        {
            if (output.Format == OutputFormat.Table)
            {
                var title = CommandText.String(entry["purpose"]) ?? "Query";
                output.WriteText(output.Settings.UseColor ? $"\u001b[1m{title}\u001b[0m" : title);
            }

            WriteResult(output, (JsonObject)entry["result"]!, skip);
            if (output.Format == OutputFormat.Table)
            {
                output.WriteText(string.Empty);
            }
        }
    }

    /// <summary>
    /// Prints a plan from <c>query build</c>. A terminal gets it to read: each item's purpose and full
    /// query, the parameters to pass, the note, and the command that runs it. CSV gets a row per item.
    /// </summary>
    /// <param name="savedTo">The file <c>--save</c> wrote the plan to, for the run command; null when it was not saved.</param>
    /// <param name="connection">The connection the build named with <c>--connection</c>, which the run command repeats; null otherwise.</param>
    /// <param name="shell">The shell the inline form is written for.</param>
    /// <param name="number">How the run commands name the plan in the library, its name or its number; null when it is not in it.</param>
    public static void WritePlan(
        OutputWriter output,
        JsonObject document,
        string? savedTo,
        string? connection = null,
        InlineShell shell = InlineShell.Posix,
        string? number = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);

        if (output.Format == OutputFormat.Json || document["result"]?["data"]?["queries"] is not JsonArray queries)
        {
            output.WriteResult(document);
            return;
        }

        if (output.Format == OutputFormat.Csv)
        {
            TabularView view = TabularView.From(queries)!;
            output.WriteCsv(view.Columns, view.Rows);
            return;
        }

        output.WriteText(DescribePlan(document, savedTo, output.Settings.UseColor, connection, shell, number));
    }

    /// <summary>The plan as a person reads it at a terminal.</summary>
    public static string DescribePlan(
        JsonObject document,
        string? savedTo,
        bool useColor,
        string? connection = null,
        InlineShell shell = InlineShell.Posix,
        string? number = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        string Heading(string text) => useColor ? $"\u001b[1m{text}\u001b[0m" : text;

        JsonNode? data = document["result"]?["data"];
        List<QueryPlanItem> items = (data?["queries"] as JsonArray ?? [])
            .Select((item, index) => QueryPlanItem.Read(item as JsonObject, index + 1, "the plan"))
            .ToList();

        var text = new StringBuilder();
        foreach (QueryPlanItem item in items)
        {
            var purpose = item.Purpose ?? "Query";
            text.AppendLine(Heading(items.Count == 1 ? purpose : $"{item.Number}. {purpose}"));
            text.AppendLine();
            if (item.IsInquiry)
            {
                text.AppendLine($"  Runs the saved inquiry '{item.InquiryName}'.");
            }
            else
            {
                foreach (var line in item.Query!.ReplaceLineEndings("\n").Split('\n'))
                {
                    text.AppendLine("  " + line);
                }
            }

            text.AppendLine();
        }

        IReadOnlyList<(string Key, bool Required)> parameters = PlanParameters(items);
        if (parameters.Count > 0)
        {
            var width = parameters.Max(parameter => parameter.Key.Length);
            text.AppendLine(Heading("Parameters"));
            foreach ((var key, var required) in parameters)
            {
                text.AppendLine($"  {key.PadRight(width)}  {(required ? "required" : "optional")}");
            }

            text.AppendLine();
        }

        if (CommandText.String(data?["note"]) is { Length: > 0 } note)
        {
            text.AppendLine(Heading("Note"));
            text.AppendLine("  " + note.Trim());
            text.AppendLine();
        }

        var arguments = string.Concat(parameters.Select(parameter => $" --param \"{parameter.Key}=...\""))
            + (connection is null ? string.Empty : $" --connection {CommandText.Quote(connection)}");
        var forced = connection is null ? string.Empty : $" --connection {CommandText.Quote(connection)}";
        text.AppendLine(Heading("Run it"));
        if (number is { } saved)
        {
            text.AppendLine(parameters.Count > 0
                ? $"  velixo query run {saved}{forced}    # asks for each value"
                : $"  velixo query run {saved}{forced}");
            if (parameters.Count > 0)
            {
                text.AppendLine($"  velixo query run {saved}{arguments}");
            }
        }

        if (savedTo is not null || number is null)
        {
            var file = savedTo ?? SuggestedPlanFile;
            text.AppendLine($"  velixo query run {(IsQueryFile(file) ? "--query" : "--plan")} {CommandText.Quote(file)}{arguments}");
        }

        // One query also runs inline, from stdin, in the form the user's shell takes.
        if (items is [{ IsInquiry: false, Query: { } query }]
            && InlineInput.Lines(shell, $"velixo query run --query -{arguments}", query, "VQL") is { } inline)
        {
            text.AppendLine();
            text.AppendLine("Or inline:");
            foreach (var line in inline)
            {
                text.AppendLine(line);
            }
        }

        return text.ToString();
    }

    /// <summary>The keys a run takes, in the plan's order: the labels a query declares, the placeholders an inquiry's parameters name.</summary>
    public static List<(string Key, bool Required)> PlanParameters(IEnumerable<QueryPlanItem> items)
    {
        var parameters = new List<(string Key, bool Required)>();
        foreach (QueryPlanItem item in items)
        {
            IEnumerable<(string Key, bool Required)> declared = item.IsInquiry
                ? (item.InquiryParameters ?? []).Select(pair => CommandText.String(pair.Value))
                    .SelectMany(value => Placeholder().Matches(value ?? string.Empty))
                    .Select(match => (match.Groups["name"].Value, true))
                : QueryDeclarations.Read(item.Query!).Select(parameter => (parameter.Key, parameter.NeedsValue));

            foreach ((var key, var required) in declared)
            {
                var index = parameters.FindIndex(parameter => parameter.Key == key);
                if (index < 0)
                {
                    parameters.Add((key, required));
                }
                else if (required)
                {
                    parameters[index] = (key, true);
                }
            }
        }

        return parameters;
    }

    [GeneratedRegex(@"\{\{(?<name>[^{}]+)\}\}")]
    private static partial Regex Placeholder();

    /// <summary>Returns whether <paramref name="document"/> holds a result with <c>columns</c> and <c>rows</c>.</summary>
    public static bool IsQueryResult(JsonNode? document) =>
        document?["result"]?["data"] is JsonObject data && data["columns"] is JsonArray && data["rows"] is JsonArray;

    /// <summary>
    /// The line under a query table, such as <c>rows 1 to 250 of 1,204; add --skip 250 or --all for the rest</c>.
    /// The hint is there whenever rows are left after the window, or the ERP says the result is partial.
    /// </summary>
    public static string? Footer(JsonObject data, int rowCount, int skip, bool paging = false)
    {
        ArgumentNullException.ThrowIfNull(data);

        long? total = ReadCount(data["totalCount"]);
        var truncated = data["truncated"] is JsonValue flag && flag.TryGetValue(out bool isTruncated) && isTruncated;
        var end = (long)skip + rowCount;
        var of = total is { } matched ? $" of {Grouped(matched)}" : string.Empty;

        var window = rowCount == 0
            ? $"no rows{(skip > 0 ? $" after row {Grouped(skip)}" : string.Empty)}{of}"
            : $"rows {Grouped(skip + 1)} to {Grouped(end)}{of}";

        return !paging && ((rowCount > 0 && end < total) || (truncated && rowCount > 0))
            ? $"{window}; add --skip {end.ToString(CultureInfo.InvariantCulture)} or --all for the rest"
            : window;
    }

    /// <summary>Reads a whole number, whether the node was parsed from text or built in code from an int or a long.</summary>
    public static long? ReadCount(JsonNode? node) =>
        node is JsonValue value && long.TryParse(value.ToJsonString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? count
            : null;

    private static string Grouped(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
