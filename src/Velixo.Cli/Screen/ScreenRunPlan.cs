using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>
/// What one <c>command run</c> sends: which command sets of the batch go out, with which values, and on
/// which record. It refuses locally what the server would refuse before running anything, so a
/// mistake costs no round trip.
/// </summary>
internal sealed class ScreenRunPlan
{
    private ScreenRunPlan(ScreenBatch batch)
    {
        Batch = batch;
    }

    public ScreenBatch Batch { get; }

    /// <summary>Gets the header set, or null on a continuation, which sends none.</summary>
    public JsonArray? Header { get; private init; }

    public JsonArray? Line { get; private init; }

    public JsonArray? Split { get; private init; }

    /// <summary>Gets the finish set when this call runs it, or null.</summary>
    public JsonArray? Finish { get; private init; }

    public JsonObject Variables { get; private init; } = [];

    public JsonArray? Rows { get; private init; }

    public ScreenContinuationSource? Continuation { get; private init; }

    public bool LeaveOpen { get; private init; }

    public IReadOnlyList<ScreenAttachment> Attachments { get; private init; } = [];

    /// <summary>
    /// Gets a value indicating whether the call can write: when <see cref="ScreenBatch.Writes"/> says the batch
    /// writes, and always for a continuation, because it adds to a record left open.
    /// </summary>
    public bool CanWrite => Continuation is not null || Batch.Writes([Header, Line, Split, Finish]);

    /// <summary>Gets the number of commands the screen runs: each set once, the detail and sub-detail sets once per row.</summary>
    public int CommandCount =>
        (Header?.Count ?? 0)
        + (Finish?.Count ?? 0)
        + ((Rows?.Count ?? 0) * (Line?.Count ?? 0))
        + (ScreenRows.CountSplits(Rows) * (Split?.Count ?? 0));

    /// <summary>
    /// Picks the sets the call sends. A first call sends the header, the detail and sub-detail sets and,
    /// unless it leaves the record open without <c>--finish</c>, the finish set. A continuation sends no
    /// header, and the finish set only with <c>--finish</c>.
    /// </summary>
    /// <exception cref="CliException">Exit 5 when the sets and the rows do not fit together, with the server's <c>SCREEN_SETS_*</c> code.</exception>
    public static ScreenRunPlan Create(
        ScreenBatch batch,
        JsonObject variables,
        JsonArray? rows,
        ScreenContinuationSource? continuation,
        bool leaveOpen,
        bool finish,
        IReadOnlyList<ScreenAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(attachments);

        if (finish && batch.FinishCommands is null)
        {
            throw CliException.Usage("--finish runs the batch's finishCommands, and this batch has none.", "Leave out --finish.");
        }

        if (rows is not null && batch.LineCommands is null)
        {
            throw SetsMisfit(
                ErrorCodes.ScreenSetsRowsWithoutLine,
                "--rows was given, but the batch carries no 'lineCommands' to run for each row. Nothing was sent.",
                "Leave out --rows, or build the batch again for a document with any number of lines.");
        }

        if (batch.LineCommands is not null && rows is null)
        {
            throw SetsMisfit(
                ErrorCodes.ScreenSetsLineWithoutRows,
                "the batch carries 'lineCommands' but no --rows was given, so the number of lines is unknown. Nothing was sent.",
                "Pass --rows FILE with one entry per line, or a file holding [] for none.");
        }

        if (batch.SplitCommands is null && rows?.Any(row => row?["rows"] is JsonArray { Count: > 0 }) == true)
        {
            throw SetsMisfit(
                ErrorCodes.ScreenSetsSplitRowsWithoutSplit,
                "a line in --rows carries sub-detail 'rows', but the batch carries no 'splitCommands' to run for them. Nothing was sent.",
                "Remove the sub-detail rows, or build the batch again for them.");
        }

        var continuing = continuation is not null;
        if (continuing && batch.LineCommands is null && !finish)
        {
            throw CliException.Usage(
                "A continuation call adds rows or finishes the record, and this one does neither.",
                "Pass --rows FILE with a batch that has lineCommands, or --finish.");
        }

        return new ScreenRunPlan(batch)
        {
            Header = continuing ? null : batch.Commands,
            Line = batch.LineCommands,
            Split = batch.SplitCommands,
            Finish = continuing ? (finish ? batch.FinishCommands : null) : (leaveOpen && !finish ? null : batch.FinishCommands),
            Variables = variables,
            Rows = rows,
            Continuation = continuation,
            LeaveOpen = leaveOpen,
            Attachments = attachments,
        };
    }

    /// <summary>
    /// Refuses, listing every problem at once, a placeholder no value binds and a value no placeholder
    /// takes. <c>--param</c> binds the header and the finish set; each row's <c>values</c> bind the detail
    /// set, and each sub-detail row's the sub-detail set.
    /// </summary>
    /// <exception cref="CliException">Exit 5 with <c>SCREEN_VARIABLE_MISSING</c> or <c>SCREEN_VARIABLE_UNKNOWN</c>.</exception>
    public void CheckBindings()
    {
        var problems = new List<string>();
        var anyMissing = false;

        var takes = ScreenPlaceholders.Names(Header).Concat(ScreenPlaceholders.Names(Finish)).Distinct(StringComparer.Ordinal).ToList();
        var missing = takes.Where(name => !Variables.ContainsKey(name)).ToList();
        var unknown = Variables.Select(pair => pair.Key).Where(name => !takes.Contains(name, StringComparer.Ordinal)).ToList();

        if (missing.Count > 0)
        {
            anyMissing = true;
            problems.Add($"The batch needs {Quote(missing)}, which no --param, --param-null or --params binds.");
        }

        if (unknown.Count > 0)
        {
            var perRow = ScreenPlaceholders.Names(Line).Concat(ScreenPlaceholders.Names(Split)).ToHashSet(StringComparer.Ordinal);
            var rowValues = unknown.Where(perRow.Contains).ToList();
            var strays = unknown.Where(name => !perRow.Contains(name)).ToList();

            if (rowValues.Count > 0)
            {
                problems.Add($"Each row takes its own value for {Quote(rowValues)}: put it in the row's values in --rows, not in --param.");
            }

            if (strays.Count > 0)
            {
                problems.Add(Continuation is not null && Finish is null
                    ? $"No placeholder takes {Quote(strays)}: a continuation without --finish sends no set that --param binds."
                    : $"No placeholder in the commands this call sends takes {Quote(strays)}.");
            }
        }

        if (Rows is not null)
        {
            IReadOnlyList<string> lineNames = ScreenPlaceholders.Names(Line);
            IReadOnlyList<string> splitNames = ScreenPlaceholders.Names(Split);
            for (var line = 0; line < Rows.Count; line++)
            {
                anyMissing |= CheckRow(Rows[line], $"line {line + 1}", lineNames, "lineCommands", problems);
                if (Rows[line]?["rows"] is JsonArray splits)
                {
                    for (var split = 0; split < splits.Count; split++)
                    {
                        anyMissing |= CheckRow(splits[split], $"line {line + 1}, split {split + 1}", splitNames, "splitCommands", problems);
                    }
                }
            }
        }

        if (problems.Count == 0)
        {
            return;
        }

        var code = anyMissing ? ErrorCodes.ScreenVariableMissing : ErrorCodes.ScreenVariableUnknown;
        throw new CliException(
            ExitCodes.ServerRefused,
            code,
            $"{code}: {string.Join(" ", problems)} Nothing was sent.",
            ErrorGuidance.For(code, string.Empty));
    }

    /// <summary>Returns the <c>execute_commands</c> arguments, with <paramref name="attachments"/> already staged.</summary>
    public JsonObject ToArguments(string intent, string? connection, JsonArray? attachments)
    {
        JsonObject arguments = Describe(connection, intent);
        if (attachments is { Count: > 0 })
        {
            arguments["attachments"] = attachments.DeepClone();
        }

        if (LeaveOpen)
        {
            arguments["leaveOpen"] = true;
        }

        return arguments;
    }

    /// <summary>Returns what the duplicate guard fingerprints: the connection, the target, the sets, the values, the rows and the record continued.</summary>
    public JsonObject Fingerprinted(string? connection) => Describe(connection, intent: null);

    public JsonObject DryRunDocument(string? connection)
    {
        var document = new JsonObject
        {
            ["dryRun"] = true,
            ["connection"] = connection,
            ["target"] = Batch.Target,
            ["commandCount"] = CommandCount,
            ["variables"] = Variables.DeepClone(),
            ["rows"] = Rows?.Count ?? 0,
            ["attachments"] = new JsonArray(Attachments.Select(file => (JsonNode)new JsonObject
            {
                ["fileName"] = file.FileName,
                ["sizeBytes"] = file.SizeBytes,
                ["comment"] = file.Comment,
            }).ToArray()),
            ["wouldWrite"] = CanWrite,
        };

        if (Continuation is not null)
        {
            document["continuation"] = Continuation.Continuation.DeepClone();
        }

        return document;
    }

    /// <summary>Returns the confirmation summary, one line each.</summary>
    public IReadOnlyList<string> Summary(string? connection)
    {
        var lines = new List<string>
        {
            $"Connection: {connection ?? "the default connection"}",
            $"Screen: {Batch.Target}",
            $"Commands: {CommandCount}",
        };

        if (Continuation is not null)
        {
            lines.Add($"Continues the open record: {Continuation.Describe()}");
        }

        if (Rows is not null)
        {
            var splits = ScreenRows.CountSplits(Rows);
            lines.Add(splits > 0 ? $"Rows: {Rows.Count} detail, {splits} sub-detail" : $"Rows: {Rows.Count}");
        }

        if (Variables.Count == 0)
        {
            lines.Add("Variables: none");
        }
        else
        {
            lines.Add("Variables:");
            lines.AddRange(Variables.Select(pair => $"  {pair.Key} = {DescribeValue(pair.Value)}"));
        }

        if (Attachments.Count > 0)
        {
            lines.Add("Attachments:");
            lines.AddRange(Attachments.Select(file =>
                $"  {file.FileName} ({file.SizeBytes:N0} bytes){(file.Comment is null ? string.Empty : $": {file.Comment}")}"));
        }

        if (Batch.KeyNumbering is { } keyNumbering)
        {
            lines.Add($"Key numbering: {CommandText.String(keyNumbering) ?? keyNumbering.ToJsonString()}");
        }

        if (LeaveOpen)
        {
            lines.Add("Leaves the record open for 'command run --continue' within 2 minutes.");
        }

        return lines;
    }

    private static string DescribeValue(JsonNode? value) =>
        value switch
        {
            null => "null (the ERP defaults the field)",
            JsonValue text when text.TryGetValue(out string? s) && s.Length == 0 => "\"\" (writes a blank)",
            _ => value.ToJsonString(),
        };

    private static string Quote(IEnumerable<string> names) => string.Join(", ", names.Select(name => $"'{name}'"));

    /// <summary>Records the problems of one row and returns whether any placeholder went unbound.</summary>
    private static bool CheckRow(JsonNode? row, string path, IReadOnlyList<string> names, string setName, List<string> problems)
    {
        JsonObject values = row?["values"] as JsonObject ?? [];
        var missing = names.Where(name => !values.ContainsKey(name)).ToList();
        var unknown = values.Select(pair => pair.Key).Where(name => !names.Contains(name, StringComparer.Ordinal)).ToList();

        if (missing.Count > 0)
        {
            problems.Add($"{path} needs {Quote(missing)}, which its values do not bind.");
        }

        if (unknown.Count > 0)
        {
            problems.Add($"{path} carries {Quote(unknown)}, which no placeholder in {setName} takes.");
        }

        return missing.Count > 0;
    }

    private static CliException SetsMisfit(string code, string message, string nextStep) =>
        new(ExitCodes.ServerRefused, code, $"{code}: {message}", nextStep);

    private JsonObject Describe(string? connection, string? intent)
    {
        var arguments = new JsonObject();
        if (intent is not null)
        {
            arguments[McpToolNames.IntentArgument] = intent;
        }

        arguments["target"] = Batch.Target;
        if (connection is not null)
        {
            arguments["connectionName"] = connection;
        }

        Add(arguments, "commands", Header);
        if (Variables.Count > 0)
        {
            arguments["variables"] = Variables.DeepClone();
        }

        Add(arguments, "lineCommands", Line);
        Add(arguments, "rows", Rows);
        Add(arguments, "splitCommands", Split);
        Add(arguments, "finishCommands", Finish);
        Add(arguments, "continuation", Continuation?.Continuation);
        return arguments;
    }

    private static void Add(JsonObject arguments, string name, JsonNode? value)
    {
        if (value is not null)
        {
            arguments[name] = value.DeepClone();
        }
    }
}
