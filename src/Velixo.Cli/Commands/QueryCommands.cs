using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Files;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Query;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo query build|run|workbook</c>: <c>prepare_query</c>, <c>execute_query</c> and
/// <c>create_workbook_from_query</c>, as the spec's section 8.3 describes them.
/// </summary>
public static class QueryCommands
{
    private const string UnorderedPagingWarning =
        "The query has no ORDER BY, so paging it with --all is not stable: a row can appear in two windows or in none.";

    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new Command("query", "Build and run ERP queries (VQL).")
        {
            BuildBuild(services),
            BuildRun(services),
            BuildWorkbook(services),
            LibraryCommands.BuildList(services, LibraryKind.Query),
            LibraryCommands.BuildDelete(services, LibraryKind.Query),
        };
    }

    private static Command BuildBuild(CliServices services)
    {
        var request = new Argument<string>("request")
        {
            Description = "The data question in the user's words, every user value as a {{Placeholder}}.",
        };
        var baseQuery = new Option<string[]>("--base-query")
        {
            Description = "A query to refine, from a file or - for stdin. Repeat it for each query of a multi-query plan.",
            HelpName = "file|-",
            Arity = ArgumentArity.ZeroOrMore,
        };

        var name = LibraryCommands.NameOption(LibraryKind.Query);
        var save = new Option<string?>("--save")
        {
            Description = "Also write the plan to this file: the query text for a .vql file (velixo query run --query FILE), the plan as JSON otherwise (--plan FILE). Replaces a file already there.",
            HelpName = "file",
        };

        var command = new Command(
            "build",
            "Build a query plan from a question. Example: velixo query build \"AR balance per customer as of {{AsOfDate}}\" --save ar.plan.json")
        {
            request,
            baseQuery,
            name,
            save,
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var intent = services.IntentOrDefault();
            var libraryName = LibraryCommands.CheckName(parse.GetValue(name));
            var inputs = new QueryInputs(services.Console, services.Environment.CurrentDirectory);

            var arguments = new JsonObject
            {
                [McpToolNames.IntentArgument] = intent,
                ["request"] = parse.GetValue(request),
            };
            AddConnection(services, arguments);

            var baseQueries = (parse.GetValue(baseQuery) ?? [])
                .Select(path => inputs.ReadQuery(path, "--base-query").Trim())
                .ToList();
            if (baseQueries.Count > 0)
            {
                arguments["baseQuery"] = string.Join("\n\n", baseQueries);
            }

            UnwrappedToolResult result = await services.Tasks.RunAsync(
                token => services.Mcp.CallToolAsync(McpToolNames.PrepareQuery, arguments, token),
                services.FollowOptions(),
                cancellationToken).ConfigureAwait(false);

            var document = (JsonObject)result.Document.DeepClone();
            QueryPlan.NormalizeItemKeys(document);
            CommandText.RelayWarnings(services.Output, document);
            // A build started with --no-wait has no plan yet, so there is nothing to keep.
            var reference = document["result"]?["data"]?["queries"] is JsonArray
                ? LibraryCommands.Save(services, LibraryKind.Query, document, parse.GetValue(request)!, libraryName)
                : null;

            string? savedTo = null;
            if (parse.GetValue(save) is { Length: > 0 } savePath)
            {
                savedTo = SavePlan(services, document, savePath);
            }

            QueryOutput.WritePlan(
                services.Output,
                document,
                savedTo,
                parse.GetValue(services.Options.Connection),
                InlineInput.Detect(services.Environment),
                reference);
            return ExitCodes.Success;
        });

        return command;
    }

    /// <summary>
    /// Writes the plan to <paramref name="savePath"/>: the query text itself for a <c>.vql</c> file, which
    /// holds one query, the whole plan as JSON otherwise. Returns the path saved to, or null when a
    /// <c>.vql</c> file cannot hold this plan, which a warning explains.
    /// </summary>
    private static string? SavePlan(CliServices services, JsonObject document, string savePath)
    {
        var path = Path.GetFullPath(savePath, services.Environment.CurrentDirectory);
        if (!QueryOutput.IsQueryFile(savePath))
        {
            File.WriteAllText(path, JsonText.Indented(document));
            services.Output.Progress($"Saved the plan to {savePath}.");
            return savePath;
        }

        var items = document["result"]?["data"]?["queries"] as JsonArray ?? [];
        if (items.Count != 1 || QueryPlanItem.Read(items[0] as JsonObject, 1, "the plan") is not { IsInquiry: false, Query: { } query })
        {
            services.Output.Warn(
                $"Not saved: a .vql file holds one query, and this plan has {(items.Count == 1 ? "a saved inquiry" : $"{items.Count} items")}. "
                + "Save it with --save NAME.json to keep the whole plan.");
            return null;
        }

        File.WriteAllText(path, query.TrimEnd() + "\n");
        services.Output.Progress($"Saved the query to {savePath}.");
        return savePath;
    }

    private static Command BuildRun(CliServices services)
    {
        var number = LibraryCommands.NumberArgument(LibraryKind.Query, "run");
        var plan = new Option<string?>("--plan") { Description = "Run every item of a saved plan, in order.", HelpName = "file" };
        var query = new Option<string?>("--query") { Description = "Run one query from a file, or - for stdin.", HelpName = "file|-" };
        var inquiry = new Option<string?>("--inquiry") { Description = "Run a saved generic inquiry as saved.", HelpName = "name" };
        var param = ParamOption();
        var parameters = ParamsOption();
        var top = new Option<int?>("--top") { Description = "Rows per call; 250 by default, 10,000 at most. Always sent.", HelpName = "n" };
        var skip = new Option<int?>("--skip") { Description = "Rows to skip before the window; 0 by default.", HelpName = "n" };
        var all = new Option<bool>("--all") { Description = "Page through every window from --skip until one comes back short, up to --max-rows." };
        var maxRows = new Option<int?>("--max-rows") { Description = "The most rows --all collects; 10,000 by default.", HelpName = "n" };

        var command = new Command(
            "run",
            "Run a built query by its number, a plan, a query or a saved inquiry. At a terminal, asks for each value not given. "
            + "Example: velixo query run 3 --param \"As Of Date=2026-09-30\"")
        {
            number,
            plan,
            query,
            inquiry,
            param,
            parameters,
            top,
            skip,
            all,
            maxRows,
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var intent = services.IntentOrDefault();
            var planPath = parse.GetValue(plan);
            var queryPath = parse.GetValue(query);
            var inquiryName = parse.GetValue(inquiry);
            var numbered = parse.GetValue(number);
            QueryPlan? fromLibrary = ChooseLibraryPlan(services, parse, numbered, planPath, queryPath, inquiryName);

            QueryRunOptions options = ReadRunOptions(services, parse.GetValue(top), parse.GetValue(skip), parse.GetValue(all), parse.GetValue(maxRows));
            var inputs = new QueryInputs(services.Console, services.Environment.CurrentDirectory);
            JsonObject given = inputs.ReadParameters(parse.GetValue(param), parse.GetValue(parameters));
            var runner = new QueryRunner(services, intent, options);

            if (fromLibrary is not null || planPath is not null)
            {
                QueryPlan read = fromLibrary ?? QueryPlan.Read(inputs.ReadJson(planPath!, "--plan"), planPath!);
                AskForMissingValues(services, QueryOutput.PlanParameters(read.Items), given);
                JsonObject combined = await RunPlanAsync(services, runner, read, given, options, cancellationToken).ConfigureAwait(false);

                // A plan of one item pages like a single query; a plan of several prints each item's window.
                if (read.Items is [var only] && QueryPager.Applies(services, options))
                {
                    BoundParameters bound = only.IsInquiry
                        ? ParameterBinder.ForInquiry(only.InquiryParameters, given)
                        : ParameterBinder.ForQuery(only.Query!, given, onlyDeclared: true);
                    await QueryPager.PageAsync(
                        services,
                        (JsonObject)combined["results"]![0]!["result"]!,
                        options.Skip,
                        options.Top,
                        only.Purpose,
                        (skip, token) => PageRunner(skip).RunAsync(only, bound.Send, token),
                        cancellationToken).ConfigureAwait(false);
                    return ExitCodes.Success;
                }

                // A plan of one query prints what --query prints for it, so a script reads both the same way.
                if (read.Items.Count == 1)
                {
                    QueryOutput.WriteResult(services.Output, (JsonObject)combined["results"]![0]!["result"]!, options.Skip);
                    return ExitCodes.Success;
                }

                QueryOutput.WritePlanResults(services.Output, combined, options.Skip);
                return ExitCodes.Success;
            }

            // The runner of one page of the same query, for the pager.
            QueryRunner PageRunner(int skip) => new(services, intent, options with { Skip = skip });

            JsonObject document;
            Func<int, CancellationToken, Task<JsonObject>> fetchPage;
            if (queryPath is not null)
            {
                var text = inputs.ReadQuery(queryPath, "--query");
                AskForMissingValues(services, QueryDeclarations.Read(text).Select(parameter => (parameter.Key, parameter.NeedsValue)), given);
                BoundParameters bound = ParameterBinder.ForQuery(text, given, onlyDeclared: false);
                WarnAll(services.Output, ParameterBinder.Warnings(given, [(null, bound)]));
                if (options.All && !QueryDeclarations.HasOrderBy(text))
                {
                    services.Output.Warn(UnorderedPagingWarning);
                }

                document = await runner.RunQueryAsync(text, bound.Send, cancellationToken).ConfigureAwait(false);
                fetchPage = (skip, token) => PageRunner(skip).RunQueryAsync(text, bound.Send, token);
            }
            else
            {
                JsonObject send = ParameterBinder.Unchecked(given).Send;
                document = await runner.RunInquiryAsync(inquiryName!, send, cancellationToken).ConfigureAwait(false);
                fetchPage = (skip, token) => PageRunner(skip).RunInquiryAsync(inquiryName!, send, token);
            }

            QueryOutput.RelayWarnings(services.Output, document);
            if (QueryPager.Applies(services, options))
            {
                await QueryPager.PageAsync(services, document, options.Skip, options.Top, title: null, fetchPage, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                QueryOutput.WriteResult(services.Output, document, options.Skip);
            }

            return ExitCodes.Success;
        });

        return command;
    }

    private static Command BuildWorkbook(CliServices services)
    {
        var number = LibraryCommands.NumberArgument(LibraryKind.Query, "turn into a workbook");
        var query = new Option<string?>("--query") { Description = "The query file, or - for stdin.", HelpName = "file|-" };
        var plan = new Option<string?>("--plan") { Description = "A saved plan.", HelpName = "file" };
        var item = new Option<int?>("--item") { Description = "Which item of the plan, from 1. Needed when the plan has more than one.", HelpName = "n" };
        var param = ParamOption();
        var parameters = ParamsOption();
        var download = new Option<string?>("--download") { Description = "Save the workbook to this file, or into this directory under its own name.", HelpName = "path" };

        var command = new Command(
            "workbook",
            "Create a Velixo workbook that holds the query as a live formula. Example: velixo query workbook 3 --download ar.xlsx")
        {
            number,
            query,
            plan,
            item,
            param,
            parameters,
            download,
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var intent = services.IntentOrDefault();
            var queryPath = parse.GetValue(query);
            var planPath = parse.GetValue(plan);
            QueryPlan? fromLibrary = ChooseLibraryPlan(services, parse, parse.GetValue(number), planPath, queryPath, inquiryName: null, verb: "turn into a workbook");
            if (planPath is null && fromLibrary is null && parse.GetValue(item) is not null)
            {
                throw CliException.Usage("--item chooses an item of a plan; it has no meaning with --query.");
            }

            var inputs = new QueryInputs(services.Console, services.Environment.CurrentDirectory);
            var text = queryPath is not null
                ? inputs.ReadQuery(queryPath, "--query")
                : ChooseWorkbookItem(fromLibrary ?? QueryPlan.Read(inputs.ReadJson(planPath!, "--plan"), planPath!), parse.GetValue(item));

            JsonObject given = inputs.ReadParameters(parse.GetValue(param), parse.GetValue(parameters));
            AskForMissingValues(services, QueryDeclarations.Read(text).Select(parameter => (parameter.Key, parameter.NeedsValue)), given);
            WarnAll(services.Output, ParameterBinder.Warnings(given, [(null, ParameterBinder.ForQuery(text, given, onlyDeclared: false))]));

            var arguments = new JsonObject
            {
                [McpToolNames.IntentArgument] = intent,
                ["query"] = text,
            };
            AddConnection(services, arguments);
            if (given.Count > 0)
            {
                arguments["parameters"] = given.ToJsonString();
            }

            UnwrappedToolResult result = await services.Tasks.RunAsync(
                token => services.Mcp.CallToolAsync(McpToolNames.CreateWorkbookFromQuery, arguments, token),
                services.FollowOptions(),
                cancellationToken).ConfigureAwait(false);

            var document = (JsonObject)result.Document.DeepClone();
            if (parse.GetValue(download) is { Length: > 0 } path)
            {
                await WorkbookDownload
                    .SaveAsync(new FileTransfer(services.Mcp, services.Http), document, Path.GetFullPath(path, services.Environment.CurrentDirectory), cancellationToken)
                    .ConfigureAwait(false);
            }

            CommandText.RelayWarnings(services.Output, document);
            services.Output.WriteResult(document);
            return ExitCodes.Success;
        });

        return command;
    }

    /// <summary>
    /// Runs every item of <paramref name="plan"/> in order with one set of values, each query given
    /// only the keys it declares, and returns <c>{ note, results: [{ purpose, result }] }</c>.
    /// </summary>
    private static async Task<JsonObject> RunPlanAsync(
        CliServices services,
        QueryRunner runner,
        QueryPlan plan,
        JsonObject given,
        QueryRunOptions options,
        CancellationToken cancellationToken)
    {
        if (services.Output.Format == OutputFormat.Csv && plan.Items.Count > 1)
        {
            throw CliException.Usage(
                $"The plan has {plan.Items.Count} items, and CSV holds one table.",
                "Use --output json or table, or run one item with --query.");
        }

        var bindings = plan.Items
            .Select(item => (Item: item, Bound: item.IsInquiry
                ? ParameterBinder.ForInquiry(item.InquiryParameters, given)
                : ParameterBinder.ForQuery(item.Query!, given, onlyDeclared: true)))
            .ToList();

        var titled = bindings.Select(binding => (plan.Items.Count > 1 ? binding.Item.Title : null, binding.Bound)).ToList();
        WarnAll(services.Output, ParameterBinder.Warnings(given, titled));

        if (options.All)
        {
            foreach (QueryPlanItem item in plan.Items.Where(item => !item.IsInquiry && !QueryDeclarations.HasOrderBy(item.Query!)))
            {
                services.Output.Warn(plan.Items.Count > 1 ? $"Plan {item.Title}: {UnorderedPagingWarning}" : UnorderedPagingWarning);
            }
        }

        var results = new JsonArray();
        foreach ((QueryPlanItem item, BoundParameters bound) in bindings)
        {
            if (plan.Items.Count > 1)
            {
                services.Output.Progress($"running {item.Title} ({item.Number} of {plan.Items.Count})");
            }

            JsonObject document = item.IsInquiry
                ? await runner.RunInquiryAsync(item.InquiryName!, bound.Send, cancellationToken).ConfigureAwait(false)
                : await runner.RunQueryAsync(item.Query!, bound.Send, cancellationToken).ConfigureAwait(false);

            QueryOutput.RelayWarnings(services.Output, document);
            results.Add((JsonNode)new JsonObject { ["purpose"] = item.Purpose, ["result"] = document });
        }

        return new JsonObject { ["note"] = plan.Note, ["results"] = results };
    }

    private static string ChooseWorkbookItem(QueryPlan plan, int? number)
    {
        QueryPlanItem item;
        if (number is { } chosen)
        {
            item = chosen >= 1 && chosen <= plan.Items.Count
                ? plan.Items[chosen - 1]
                : throw CliException.Usage($"--item {chosen} is not an item of the plan, which has {plan.Items.Count}.");
        }
        else if (plan.Items.Count == 1)
        {
            item = plan.Items[0];
        }
        else
        {
            throw CliException.Usage(
                $"The plan has {plan.Items.Count} items; choose one with --item: {string.Join("; ", plan.Items.Select(candidate => candidate.Title))}.");
        }

        return item.IsInquiry
            ? throw CliException.Usage(
                $"Plan {item.Title} runs the saved inquiry '{item.InquiryName}' as saved, which carries no query to put in a workbook.",
                "Build again with velixo query build, asking for that inquiry as a workbook; the builder then returns a VQL query over the inquiry.")
            : item.Query!;
    }

    private static QueryRunOptions ReadRunOptions(CliServices services, int? top, int? skip, bool all, int? maxRows)
    {
        if (top is < 1 or > QueryRunOptions.MaxTop)
        {
            throw CliException.Usage("--top must be between 1 and 10,000; the server returns at most that many rows per call.");
        }

        if (skip is < 0)
        {
            throw CliException.Usage("--skip must be 0 or more.");
        }

        if (maxRows is < 1)
        {
            throw CliException.Usage("--max-rows must be 1 or more.");
        }

        if (all && services.Settings.NoWait)
        {
            throw CliException.Usage("--all reads every window, so it cannot return before the first one finishes; drop --no-wait.");
        }

        return new QueryRunOptions
        {
            Top = top ?? QueryRunOptions.DefaultTop,
            Skip = skip ?? 0,
            All = all,
            MaxRows = maxRows ?? QueryRunOptions.DefaultMaxRows,
        };
    }

    /// <summary>
    /// Returns the library plan the command runs: the one numbered <paramref name="number"/>, or, when no
    /// source is named at all, the one a person picks on a terminal. Null when a flag names the source.
    /// </summary>
    private static QueryPlan? ChooseLibraryPlan(
        CliServices services,
        ParseResult parse,
        string? number,
        string? planPath,
        string? queryPath,
        string? inquiryName,
        string verb = "run")
    {
        (string Flag, string? Value)[] flags = inquiryName is null && verb != "run"
            ? [("--plan", planPath), ("--query", queryPath)]
            : [("--plan", planPath), ("--query", queryPath), ("--inquiry", inquiryName)];
        var named = flags.Where(source => source.Value is not null).Select(source => source.Flag).ToList();
        if (number is not null && named.Count > 0)
        {
            throw CliException.Usage($"Give a query number or {string.Join(" and ", named)}, not both.");
        }

        if (number is null && named.Count > 0)
        {
            RequireOneSource(flags);
            return null;
        }

        if (number is null && !ValuePrompt.CanAsk(services))
        {
            throw CliException.Usage(
                $"Name what to {verb}: a number from 'velixo query list', or one of {string.Join(", ", flags.Select(source => source.Flag))}.",
                "velixo query list");
        }

        LibraryEntry entry = LibraryCommands.Find(services, LibraryKind.Query, number, verb, parse.GetValue(services.Options.Connection));
        return QueryPlan.Read(entry.Document, $"query {entry.Number}");
    }

    /// <summary>
    /// Asks a person at a terminal for every declared value <paramref name="given"/> does not hold, and
    /// adds the answers to it. A required value must be answered; Enter skips an optional one.
    /// </summary>
    private static void AskForMissingValues(CliServices services, IEnumerable<(string Key, bool Required)> declared, JsonObject given)
    {
        if (!ValuePrompt.CanAsk(services))
        {
            return;
        }

        var missing = declared.Where(parameter => !given.ContainsKey(parameter.Key)).DistinctBy(parameter => parameter.Key).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        services.Console.Error.WriteLine("Values for the query:");
        foreach ((var key, var required) in missing)
        {
            PromptAnswer answer = ValuePrompt.Ask(services, key, about: null, required);
            if (!answer.Skipped)
            {
                given[key] = answer.Value;
            }
        }
    }

    private static void RequireOneSource(params (string Flag, string? Value)[] sources)
    {
        var given = sources.Where(source => source.Value is not null).Select(source => source.Flag).ToList();
        if (given.Count != 1)
        {
            var flags = string.Join(", ", sources.Select(source => source.Flag));
            throw CliException.Usage(given.Count == 0
                ? $"Name what to run with one of {flags}."
                : $"Give one of {flags}, not {string.Join(" and ", given)}.");
        }
    }

    private static void AddConnection(CliServices services, JsonObject arguments)
    {
        if (services.Settings.Connection is { } connection)
        {
            arguments["connectionName"] = connection;
        }
    }

    private static void WarnAll(OutputWriter output, IEnumerable<string> warnings)
    {
        foreach (var warning in warnings)
        {
            output.Warn(warning);
        }
    }

    private static Option<string[]> ParamOption() => new("--param")
    {
        Description = "A parameter by its display label, the text after AS in its DECLARE: \"As Of Date=2026-09-30\". Repeatable; wins over --params.",
        HelpName = "label=value",
        Arity = ArgumentArity.ZeroOrMore,
    };

    private static Option<string?> ParamsOption() => new("--params")
    {
        Description = "The parameters as a JSON object in a file, or - for stdin, keyed by display label.",
        HelpName = "file|-",
    };
}
