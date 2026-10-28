using System.Globalization;
using System.Text.Json.Nodes;

namespace Velixo.Cli.Output;

/// <summary>
/// A result document seen as columns and rows, so the table and CSV formats can print it. It
/// recognises the shapes the tools return: a query result (<c>columns</c> with captions and
/// <c>rows</c> of arrays), an array of objects, and any object as key and value.
/// </summary>
public sealed class TabularView
{
    required public IReadOnlyList<string> Columns { get; init; }

    required public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }

    /// <summary>Gets the line printed under a table, such as the window of a query result.</summary>
    public string? Footer { get; init; }

    /// <summary>
    /// Reads <paramref name="document"/> as a table, descending into <c>result</c> and <c>data</c> first.
    /// Returns null for a document that is a single scalar.
    /// </summary>
    public static TabularView? From(JsonNode? document)
    {
        JsonNode? node = Descend(document);

        return node switch
        {
            JsonObject query when query["columns"] is JsonArray columns && query["rows"] is JsonArray rows =>
                FromQueryResult(query, columns, rows),
            JsonArray array => FromArray(array),
            JsonObject obj => FromObject(obj),
            _ => null,
        };
    }

    /// <summary>Writes one cell: a string as itself, any other scalar as JSON, an object or array as compact JSON.</summary>
    public static string CellText(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue value when value.TryGetValue(out string? text) => text ?? string.Empty,
        JsonValue value => value.ToJsonString(),
        _ => JsonText.Compact(node),
    };

    private static JsonNode? Descend(JsonNode? node)
    {
        while (node is JsonObject obj)
        {
            if (obj["result"] is JsonObject result)
            {
                node = result;
            }
            else if (obj.ContainsKey("data") && obj["data"] is JsonObject or JsonArray)
            {
                node = obj["data"];
            }
            else
            {
                break;
            }
        }

        return node;
    }

    private static TabularView FromQueryResult(JsonObject query, JsonArray columns, JsonArray rows)
    {
        var captions = columns
            .Select((column, index) => column switch
            {
                JsonObject described => CellText(described["caption"] ?? described["fieldName"] ?? described["name"]),
                JsonValue named => CellText(named),
                _ => $"column{index + 1}",
            })
            .ToList();

        var cells = rows
            .Select(row => (IReadOnlyList<string>)(row is JsonArray values
                ? values.Select(CellText).ToList()
                : new List<string> { CellText(row) }))
            .ToList();

        string? footer = null;
        if (query["totalCount"] is JsonValue total && total.TryGetValue(out long totalCount))
        {
            footer = cells.Count == 0
                ? $"no rows of {totalCount.ToString("N0", CultureInfo.InvariantCulture)}"
                : $"rows 1 to {cells.Count.ToString("N0", CultureInfo.InvariantCulture)} of {totalCount.ToString("N0", CultureInfo.InvariantCulture)}";
        }

        return new TabularView { Columns = captions, Rows = cells, Footer = footer };
    }

    private static TabularView FromArray(JsonArray array)
    {
        if (array.Count > 0 && array.All(item => item is JsonObject))
        {
            var columns = new List<string>();
            foreach (JsonObject item in array.Cast<JsonObject>())
            {
                foreach (var key in item.Select(property => property.Key))
                {
                    if (!columns.Contains(key, StringComparer.Ordinal))
                    {
                        columns.Add(key);
                    }
                }
            }

            var rows = array
                .Cast<JsonObject>()
                .Select(item => (IReadOnlyList<string>)columns.Select(column => CellText(item[column])).ToList())
                .ToList();

            return new TabularView { Columns = columns, Rows = rows };
        }

        return new TabularView
        {
            Columns = ["value"],
            Rows = array.Select(item => (IReadOnlyList<string>)[CellText(item)]).ToList(),
        };
    }

    private static TabularView FromObject(JsonObject obj) => new()
    {
        Columns = ["key", "value"],
        Rows = obj.Select(property => (IReadOnlyList<string>)[property.Key, CellText(property.Value)]).ToList(),
    };
}
