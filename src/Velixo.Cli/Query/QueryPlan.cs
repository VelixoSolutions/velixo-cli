using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Query;

/// <summary>
/// A plan from <c>prepare_query</c>: one or more items to run in order, and a note that says how to
/// combine their results. The CLI names an item's query text <c>query</c> on output; the server still
/// names it <c>giql</c>, and both are read.
/// </summary>
public sealed class QueryPlan
{
    /// <summary>The key the CLI prints an item's query text under.</summary>
    public const string QueryKey = "query";

    /// <summary>The key the server sends an item's query text under today.</summary>
    public const string ServerQueryKey = "giql";

    public string? Note { get; init; }

    required public IReadOnlyList<QueryPlanItem> Items { get; init; }

    /// <summary>
    /// Reads a saved plan: the whole document <c>velixo query build</c> printed, or only its
    /// <c>result.data</c> object.
    /// </summary>
    /// <exception cref="CliException">Exit 2 when the document holds no plan.</exception>
    public static QueryPlan Read(JsonNode? document, string source)
    {
        JsonObject? plan = document switch
        {
            JsonObject whole when whole["result"]?["data"] is JsonObject data => data,
            JsonObject whole when whole["data"] is JsonObject data => data,
            JsonObject bare => bare,
            _ => null,
        };

        if (plan?["queries"] is not JsonArray queries || queries.Count == 0)
        {
            throw CliException.Usage(
                $"'{source}' holds no plan: expected the output of velixo query build, with a non-empty 'queries' array.",
                "Save a plan with: velixo query build \"...\" --output json > my.plan.json");
        }

        var items = queries
            .Select((item, index) => QueryPlanItem.Read(item as JsonObject, index + 1, source))
            .ToList();

        return new QueryPlan { Note = (string?)(plan["note"] as JsonValue), Items = items };
    }

    /// <summary>
    /// Renames <c>giql</c> to <c>query</c> in every item of the plan a document carries under
    /// <c>result.data</c>, keeping the order of the keys. A document that is not a plan is left as it is.
    /// </summary>
    public static void NormalizeItemKeys(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document["result"]?["data"]?["queries"] is not JsonArray queries)
        {
            return;
        }

        for (var index = 0; index < queries.Count; index++)
        {
            if (queries[index] is JsonObject item && item.ContainsKey(ServerQueryKey) && !item.ContainsKey(QueryKey))
            {
                var renamed = new JsonObject();
                foreach ((var key, JsonNode? value) in item)
                {
                    renamed[key == ServerQueryKey ? QueryKey : key] = value?.DeepClone();
                }

                queries[index] = renamed;
            }
        }
    }
}
