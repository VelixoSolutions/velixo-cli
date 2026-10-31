using System.Globalization;
using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;

namespace Velixo.Cli.Query;

/// <summary>
/// Runs query text or a saved inquiry through <c>execute_query</c>, one window or, with <c>--all</c>,
/// every window merged into one result. All the calls of one command share its <c>--timeout</c>
/// through <see cref="CliServices.FollowOptions"/>.
/// </summary>
internal sealed class QueryRunner
{
    /// <summary>The <c>execute_query</c> argument for query text; the server still calls it <c>giql</c>.</summary>
    public const string QueryTextArgument = "giql";

    public const string InquiryArgument = "inquiry";

    private readonly CliServices _services;
    private readonly string _intent;
    private readonly QueryRunOptions _options;

    public QueryRunner(CliServices services, string intent, QueryRunOptions options)
    {
        _services = services;
        _intent = intent;
        _options = options;
    }

    public Task<JsonObject> RunQueryAsync(string query, JsonObject parameters, CancellationToken cancellationToken) =>
        RunAsync(QueryTextArgument, query, parameters, cancellationToken);

    /// <summary>Runs one item of a plan: its query, or its saved inquiry.</summary>
    public Task<JsonObject> RunAsync(QueryPlanItem item, JsonObject parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.IsInquiry
            ? RunInquiryAsync(item.InquiryName!, parameters, cancellationToken)
            : RunQueryAsync(item.Query!, parameters, cancellationToken);
    }

    public Task<JsonObject> RunInquiryAsync(string name, JsonObject parameters, CancellationToken cancellationToken) =>
        RunAsync(InquiryArgument, name, parameters, cancellationToken);

    private async Task<JsonObject> RunAsync(string sourceArgument, string source, JsonObject parameters, CancellationToken cancellationToken)
    {
        if (!_options.All)
        {
            return (await CallAsync(sourceArgument, source, parameters, _options.Top, _options.Skip, cancellationToken).ConfigureAwait(false)).Document;
        }

        JsonObject? first = null;
        JsonObject? firstData = null;
        var rows = new JsonArray();
        var warnings = new List<string>();
        var skip = _options.Skip;
        long? totalCount = null;

        while (true)
        {
            var top = Math.Min(_options.Top, _options.MaxRows - rows.Count);
            if (first is not null)
            {
                _services.Output.Progress($"fetching rows {Grouped(skip + 1)} to {Grouped(skip + top)}");
            }

            UnwrappedToolResult page = await CallAsync(sourceArgument, source, parameters, top, skip, cancellationToken).ConfigureAwait(false);
            if (page.Data is not JsonObject data || data["rows"] is not JsonArray pageRows)
            {
                if (first is null)
                {
                    return page.Document;
                }

                break;
            }

            first ??= page.Document;
            firstData ??= data;
            foreach (JsonNode? row in pageRows)
            {
                rows.Add(row?.DeepClone());
            }

            CollectWarnings(data, warnings);
            totalCount = QueryOutput.ReadCount(data["totalCount"]) ?? totalCount;

            skip += pageRows.Count;
            if (pageRows.Count < top || rows.Count >= _options.MaxRows || skip >= totalCount)
            {
                break;
            }
        }

        var merged = (JsonObject)first.DeepClone();
        merged["result"]!["data"] = Merge(firstData!, rows, totalCount, warnings);
        return merged;
    }

    private async Task<UnwrappedToolResult> CallAsync(
        string sourceArgument,
        string source,
        JsonObject parameters,
        int top,
        int skip,
        CancellationToken cancellationToken)
    {
        var arguments = new JsonObject
        {
            [McpToolNames.IntentArgument] = _intent,
            [sourceArgument] = source,
            ["top"] = top,
            ["skip"] = skip,
        };

        if (parameters.Count > 0)
        {
            arguments["parameters"] = parameters.DeepClone();
        }

        if (_services.Settings.Connection is { } connection)
        {
            arguments["connectionName"] = connection;
        }

        return await _services.Tasks
            .RunAsync(token => _services.Mcp.CallToolAsync(McpToolNames.ExecuteQuery, arguments, token), _services.FollowOptions(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// One result from every window: the first window's columns, every row, one <c>totalCount</c>.
    /// A window's own <c>warning</c> and <c>truncated</c> are dropped, and set again only when
    /// <c>--max-rows</c> stopped the walk before the last row.
    /// </summary>
    private JsonObject Merge(JsonObject firstData, JsonArray rows, long? totalCount, List<string> warnings)
    {
        var data = new JsonObject();
        var end = _options.Skip + rows.Count;
        if (totalCount is { } total && end < total)
        {
            data["warning"] = $"--max-rows {Grouped(_options.MaxRows)} was reached: rows {Grouped(_options.Skip + 1)} to {Grouped(end)} of {Grouped(total)} were returned.";
            data["truncated"] = true;
        }

        foreach ((var key, JsonNode? value) in firstData)
        {
            if (key is not ("warning" or "truncated" or "rows" or "totalCount" or "warnings"))
            {
                data[key] = value?.DeepClone();
            }
        }

        data["rows"] = rows;
        if (totalCount is not null)
        {
            data["totalCount"] = totalCount;
        }

        if (warnings.Count > 0)
        {
            data["warnings"] = new JsonArray(warnings.Select(warning => (JsonNode?)warning).ToArray());
        }

        return data;
    }

    private static void CollectWarnings(JsonObject data, List<string> warnings)
    {
        if (data["warnings"] is not JsonArray pageWarnings)
        {
            return;
        }

        foreach (JsonNode? warning in pageWarnings)
        {
            var text = warning is JsonValue value && value.TryGetValue(out string? plain) ? plain : warning?.ToJsonString();
            if (text is not null && !warnings.Contains(text, StringComparer.Ordinal))
            {
                warnings.Add(text);
            }
        }
    }

    private static string Grouped(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
