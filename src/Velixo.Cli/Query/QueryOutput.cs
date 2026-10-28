using System.Globalization;
using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Output;

namespace Velixo.Cli.Query;

/// <summary>
/// Prints what the query commands return. A query result prints its rows under their captions in
/// table and CSV mode, and its table ends with a footer naming the window; a plan prints its items.
/// Anything else prints the way every command prints a document.
/// </summary>
internal static class QueryOutput
{
    /// <summary>
    /// Repeats on stderr what a query result says about itself: a partial window, the ERP's warnings,
    /// and the warnings the tool sent beside its result.
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
            output.Warn(warning);
        }
        else if (data["truncated"] is JsonValue truncated && truncated.TryGetValue(out bool isTruncated) && isTruncated)
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
    public static void WriteResult(OutputWriter output, JsonObject document, int skip)
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
            output.WriteTable(view.Columns, view.Rows, Footer((JsonObject)document["result"]!["data"]!, view.Rows.Count, skip));
        }
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

        var results = combined["results"] as JsonArray ?? [];
        if (output.Format == OutputFormat.Table && CommandText.String(combined["note"]) is { Length: > 0 } note)
        {
            output.WriteText($"note: {note}\n");
        }

        foreach (JsonObject entry in results.OfType<JsonObject>())
        {
            if (output.Format == OutputFormat.Table)
            {
                output.WriteText($"== {CommandText.String(entry["purpose"]) ?? "query"} ==");
            }

            WriteResult(output, (JsonObject)entry["result"]!, skip);
            if (output.Format == OutputFormat.Table)
            {
                output.WriteText(string.Empty);
            }
        }
    }

    /// <summary>
    /// Prints a plan from <c>query build</c>: its items as a table of purpose and query, the note
    /// under it, in table and CSV mode.
    /// </summary>
    public static void WritePlan(OutputWriter output, JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);

        if (output.Format == OutputFormat.Json || document["result"]?["data"]?["queries"] is not JsonArray queries)
        {
            output.WriteResult(document);
            return;
        }

        TabularView view = TabularView.From(queries)!;
        if (output.Format == OutputFormat.Csv)
        {
            output.WriteCsv(view.Columns, view.Rows);
            return;
        }

        var note = CommandText.String(document["result"]!["data"]!["note"]);
        output.WriteTable(view.Columns, view.Rows, string.IsNullOrWhiteSpace(note) ? null : $"note: {note}");
    }

    /// <summary>Returns whether <paramref name="document"/> holds a result with <c>columns</c> and <c>rows</c>.</summary>
    public static bool IsQueryResult(JsonNode? document) =>
        document?["result"]?["data"] is JsonObject data && data["columns"] is JsonArray && data["rows"] is JsonArray;

    /// <summary>
    /// The line under a query table, such as <c>rows 1 to 250 of 1,204; add --skip 250 or --all for the rest</c>.
    /// The hint is there whenever rows are left after the window, or the ERP says the result is partial.
    /// </summary>
    public static string? Footer(JsonObject data, int rowCount, int skip)
    {
        ArgumentNullException.ThrowIfNull(data);

        long? total = ReadCount(data["totalCount"]);
        var truncated = data["truncated"] is JsonValue flag && flag.TryGetValue(out bool isTruncated) && isTruncated;
        var end = (long)skip + rowCount;
        var of = total is { } matched ? $" of {Grouped(matched)}" : string.Empty;

        var window = rowCount == 0
            ? $"no rows{(skip > 0 ? $" after row {Grouped(skip)}" : string.Empty)}{of}"
            : $"rows {Grouped(skip + 1)} to {Grouped(end)}{of}";

        return (rowCount > 0 && end < total) || (truncated && rowCount > 0)
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
