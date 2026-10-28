using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Query;

/// <summary>One item of a <see cref="QueryPlan"/>: either query text to run, or a saved inquiry to run as saved.</summary>
public sealed class QueryPlanItem
{
    /// <summary>Gets the item's position in the plan, from 1.</summary>
    public int Number { get; init; }

    public string? Purpose { get; init; }

    /// <summary>Gets the query text, or null for an item that runs a saved inquiry.</summary>
    public string? Query { get; init; }

    public string? InquiryName { get; init; }

    /// <summary>Gets the inquiry's parameters as the plan gives them, values holding <c>{{Placeholders}}</c>.</summary>
    public JsonObject? InquiryParameters { get; init; }

    public bool IsInquiry => Query is null;

    /// <summary>Gets the item as a progress line names it: its number and its purpose.</summary>
    public string Title => string.IsNullOrWhiteSpace(Purpose) ? $"item {Number}" : $"item {Number}, {Purpose}";

    internal static QueryPlanItem Read(JsonObject? item, int number, string source)
    {
        var query = Text(item?[QueryPlan.QueryKey]) ?? Text(item?[QueryPlan.ServerQueryKey]);
        JsonObject? inquiry = item?["inquiry"] as JsonObject;
        var inquiryName = Text(inquiry?["name"]);

        if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(inquiryName))
        {
            throw CliException.Usage($"Item {number} of the plan in '{source}' has neither a 'query' nor an 'inquiry.name'.");
        }

        return new QueryPlanItem
        {
            Number = number,
            Purpose = Text(item?["purpose"]),
            Query = string.IsNullOrWhiteSpace(query) ? null : query,
            InquiryName = string.IsNullOrWhiteSpace(query) ? inquiryName : null,
            InquiryParameters = string.IsNullOrWhiteSpace(query) ? inquiry?["parameters"]?.DeepClone() as JsonObject : null,
        };
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
