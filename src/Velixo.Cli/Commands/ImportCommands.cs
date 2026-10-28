using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Files;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo import scenarios|run</c>: <c>list_import_scenarios</c> and <c>execute_import_scenario</c>
/// on Acumatica.
/// </summary>
public static class ImportCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new Command("import", "Run Acumatica import scenarios. --connection is required.")
        {
            BuildScenarios(services),
            BuildRun(services),
        };
    }

    private static Command BuildScenarios(CliServices services)
    {
        var screen = new Option<string?>("--screen") { Description = "Only the scenarios of this screen.", HelpName = "screen-id" };
        var filter = new Option<string?>("--filter") { Description = "Only the scenarios whose name matches.", HelpName = "pattern" };

        var command = new Command("scenarios", "List the import scenarios and their columns. Example: velixo import scenarios --connection gab-dev") { screen, filter };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var arguments = new JsonObject { ["connectionName"] = RequireConnection(services) };
            if (parse.GetValue(screen) is { Length: > 0 } screenId)
            {
                arguments["screenId"] = screenId;
            }

            if (parse.GetValue(filter) is { Length: > 0 } pattern)
            {
                arguments["nameFilter"] = pattern;
            }

            McpToolResult called = await services.Mcp.CallToolAsync(McpToolNames.ListImportScenarios, arguments, cancellationToken).ConfigureAwait(false);
            UnwrappedToolResult listing = ToolResultUnwrapper.Unwrap(called);
            CommandText.RelayWarnings(services.Output, listing.Document);

            if (ReadScenarios(listing.Data) is not { } scenarios)
            {
                services.Output.WriteResult(listing.Document);
                return ExitCodes.Success;
            }

            WriteScenarios(services.Output, scenarios);
            return ExitCodes.Success;
        });
        return command;
    }

    private static Command BuildRun(CliServices services)
    {
        var scenario = new Option<string>("--scenario") { Description = "The import scenario's name.", HelpName = "name", Required = true };
        var file = new Option<string>("--file") { Description = "The file to import.", HelpName = "path", Required = true };
        var sheet = new Option<string?>("--sheet") { Description = "The worksheet of an Excel file.", HelpName = "name" };
        var range = new Option<string?>("--range") { Description = "The cells to import, such as A1:F200.", HelpName = "range" };
        var validateOnly = new Option<bool>("--validate-only") { Description = "Stop after the validation preview." };
        var breakOnError = new Option<bool>("--break-on-error") { Description = "Stop at the first row that fails." };

        var command = new Command(
            "run",
            "Validate, confirm and run an import. Example: velixo import run --connection gab-dev --scenario \"Import Customers\" --file customers.csv --yes --intent \"...\"")
        {
            scenario,
            file,
            sheet,
            range,
            validateOnly,
            breakOnError,
        };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var connection = RequireConnection(services);
            var intent = services.RequireIntent();
            var scenarioName = parse.GetValue(scenario)!;
            var filePath = Path.GetFullPath(parse.GetValue(file)!, services.Environment.CurrentDirectory);

            services.Output.Progress($"staging {Path.GetFileName(filePath)}");
            StagedFile staged = await new FileTransfer(services.Mcp, services.Http).StageAsync(filePath, comment: null, cancellationToken).ConfigureAwait(false);

            JsonObject Arguments(bool previewOnly)
            {
                var arguments = new JsonObject
                {
                    [McpToolNames.IntentArgument] = intent,
                    ["connectionName"] = connection,
                    ["scenarioName"] = scenarioName,
                    ["fileName"] = staged.FileName,
                    ["fileToken"] = staged.FileToken,
                };

                if (parse.GetValue(sheet) is { Length: > 0 } sheetName)
                {
                    arguments["sheetName"] = sheetName;
                }

                if (parse.GetValue(range) is { Length: > 0 } cells)
                {
                    arguments["range"] = cells;
                }

                if (parse.GetValue(breakOnError))
                {
                    arguments["breakOnError"] = true;
                }

                if (previewOnly)
                {
                    arguments["validateOnly"] = true;
                }

                return arguments;
            }

            services.Output.Progress("validating the file against the scenario");
            Task<UnwrappedToolResult> ExecuteAsync(JsonObject arguments, TaskFollowOptions options) =>
                services.Tasks.RunAsync(token => services.Mcp.CallToolAsync(McpToolNames.ExecuteImportScenario, arguments, token), options, cancellationToken);

            UnwrappedToolResult previewed = await ExecuteAsync(
                Arguments(previewOnly: true),
                services.FollowOptions() with { NoWait = false }).ConfigureAwait(false);
            JsonObject preview = PromoteTextualResponse(previewed.Document);
            CommandText.RelayWarnings(services.Output, preview);
            ImportPreview summary = ImportPreview.From(preview, scenarioName);
            Describe(services.Output, summary, connection);

            if (parse.GetValue(validateOnly))
            {
                services.Output.WriteResult(preview);
                return ExitCodes.Success;
            }

            Confirmation.Require(
                services,
                summary: [],
                question: $"Import {summary.RowsText} with scenario {summary.Scenario} into screen {summary.ScreenText} on connection {connection}?",
                notAsked: "The import was not run: it writes records, and there is no terminal to ask for a confirmation.",
                notAskedNextStep: "Show the user the preview above and ask them. After they approve, run the command again with --yes. --validate-only prints the preview and stops.",
                declined: "The import was not confirmed, so nothing was imported.");

            services.Output.Progress("running the import");
            UnwrappedToolResult ran = await ExecuteAsync(Arguments(previewOnly: false), services.FollowOptions()).ConfigureAwait(false);
            JsonObject report = PromoteTextualResponse(ran.Document);
            CommandText.RelayWarnings(services.Output, report);
            if (!ran.IsRunning)
            {
                WarnAboutRunResult(services.Output, report, summary.Warnings);
            }

            services.Output.WriteResult(report);
            return ExitCodes.Success;
        });
        return command;
    }

    private static string RequireConnection(CliServices services) =>
        services.Settings.Connection
        ?? throw CliException.Usage(
            "The import commands need --connection: the import tools write records, so the connection is one you state and not one the server picks.",
            "velixo connections list");

    /// <summary>
    /// The validation preview is answered as plain text holding <c>{ "textualResponse": "{...}" }</c>,
    /// not as a task, so the tool result unwrapper leaves the inner document as a string. This parses it
    /// into <c>result.data</c> the way a finished task's result is.
    /// </summary>
    private static JsonObject PromoteTextualResponse(JsonObject document)
    {
        if (document["result"] is JsonObject result
            && result["data"] is JsonObject data
            && CommandText.String(data["textualResponse"]) is { } text
            && TryParse(text) is { } parsed)
        {
            result["data"] = parsed;
        }

        return document;
    }

    private static JsonNode? TryParse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Describe(OutputWriter output, ImportPreview preview, string connection)
    {
        output.Progress($"scenario:   {preview.Scenario}");
        output.Progress(preview.ScreenTitle is null ? $"screen:     {preview.ScreenText}" : $"screen:     {preview.ScreenText} ({preview.ScreenTitle})");
        output.Progress($"connection: {connection}");
        output.Progress($"rows:       {preview.RowsText}");
        foreach (var warning in preview.Warnings)
        {
            output.Warn(warning);
        }

        output.Progress("An import is not atomic and has no undo: rows the ERP accepts stay imported even when a later row fails.");
    }

    /// <summary>
    /// A report with failed rows still exits 0, so the stderr warning is what tells the caller to read
    /// the ERP back. Warnings the preview already showed are not repeated.
    /// </summary>
    private static void WarnAboutRunResult(OutputWriter output, JsonObject report, IReadOnlyList<string> shownInPreview)
    {
        JsonObject? data = (report["result"] as JsonObject)?["data"] as JsonObject;

        if (data?["warnings"] is JsonArray warnings)
        {
            foreach (var warning in warnings.Select(CommandText.String).OfType<string>().Where(warning => !shownInPreview.Contains(warning)))
            {
                output.Warn(warning);
            }
        }

        var failed = FailedRows(data);
        if (failed > 0)
        {
            var total = Count(data?["totalRows"]) ?? Count(data?["rowsTotal"]) ?? (data?["rows"] as JsonArray)?.Count;
            var ofTotal = total is { } all ? $" of {all.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
            var notAttempted = Count(data?["notAttempted"]) is { } skipped and > 0
                ? $" {skipped.ToString(CultureInfo.InvariantCulture)} more were not attempted."
                : string.Empty;
            output.Warn(
                $"{failed.ToString(CultureInfo.InvariantCulture)}{ofTotal} rows failed.{notAttempted} "
                + "The import is not atomic: rows the ERP accepted stay imported, and a row reported as failed may still have written a record. "
                + "Read the ERP back before reporting the result.");
        }
    }

    private static int FailedRows(JsonObject? data)
    {
        if (Count(data?["failed"]) is { } failed)
        {
            return failed;
        }

        return data?["rows"] is JsonArray rows ? rows.OfType<JsonObject>().Count(row => row["error"] is not null) : 0;
    }

    private static int? Count(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out int count) ? count : null;

    private static JsonArray? ReadScenarios(JsonNode? listing)
    {
        JsonArray? entries = listing switch
        {
            JsonArray array => array,
            JsonObject wrapper => wrapper.FirstOrDefault(property => string.Equals(property.Key, "scenarios", StringComparison.OrdinalIgnoreCase)).Value as JsonArray,
            _ => null,
        };

        if (entries is null)
        {
            return null;
        }

        return new JsonArray(entries.OfType<JsonObject>().Select(ScenarioOf).ToArray<JsonNode?>());
    }

    private static JsonObject ScenarioOf(JsonObject entry)
    {
        var apiEnabled = Property(entry, "apiEnabled") is JsonValue flag && flag.TryGetValue(out bool enabled) && enabled;
        var scenario = new JsonObject
        {
            ["name"] = CommandText.String(Property(entry, "name")),
            ["screenId"] = CommandText.String(Property(entry, "screenId")),
            ["apiEnabled"] = apiEnabled,
        };

        if (apiEnabled && Property(entry, "columns") is JsonArray columns)
        {
            scenario["columns"] = columns.DeepClone();
        }

        return scenario;
    }

    private static JsonNode? Property(JsonObject entry, string name) =>
        entry.FirstOrDefault(property => string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static void WriteScenarios(OutputWriter output, JsonArray scenarios)
    {
        if (output.Format == OutputFormat.Json)
        {
            output.WriteJson(scenarios);
            return;
        }

        string[] captions = ["name", "screen", "apiEnabled", "columns"];
        IEnumerable<IReadOnlyList<string?>> rows = scenarios.OfType<JsonObject>().Select(scenario => (IReadOnlyList<string?>)
        [
            CommandText.String(scenario["name"]),
            CommandText.String(scenario["screenId"]),
            TabularView.CellText(scenario["apiEnabled"]),
            scenario["columns"] is JsonArray columns ? string.Join(", ", columns.Select(CommandText.String)) : null,
        ]);

        if (output.Format == OutputFormat.Csv)
        {
            output.WriteCsv(captions, rows);
        }
        else
        {
            output.WriteTable(captions, rows);
        }
    }

    /// <summary>What the validation preview says about the import, with a stand-in for anything it left out.</summary>
    private sealed record ImportPreview(string Scenario, string? ScreenId, string? ScreenTitle, int? Rows, IReadOnlyList<string> Warnings)
    {
        public string ScreenText => ScreenId ?? "unknown";

        public string RowsText => Rows is { } count ? $"{count.ToString(CultureInfo.InvariantCulture)} rows" : "an unknown number of rows";

        public static ImportPreview From(JsonObject document, string requestedScenario)
        {
            var data = (document["result"] as JsonObject)?["data"] as JsonObject;
            return new ImportPreview(
                CommandText.String(data?["scenario"]) ?? requestedScenario,
                CommandText.String(data?["screenId"]),
                CommandText.String(data?["screenTitle"]),
                Count((data?["file"] as JsonObject)?["rows"]),
                (data?["warnings"] as JsonArray)?.Select(CommandText.String).OfType<string>().ToList() ?? []);
        }
    }
}
