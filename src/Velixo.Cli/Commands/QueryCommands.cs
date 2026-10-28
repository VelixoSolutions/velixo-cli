using System.CommandLine;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo query build|run|workbook</c>: <c>prepare_query</c>, <c>execute_query</c> and
/// <c>create_workbook_from_query</c>. Declared with the options of the spec, section 8.3; not
/// implemented in this build.
/// </summary>
public static class QueryCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new Command("query", "Build and run ERP queries (VQL).")
        {
            BuildBuild(),
            BuildRun(),
            BuildWorkbook(),
        };
    }

    private static Command BuildBuild()
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

        var command = new Command(
            "build",
            "Build a query plan with prepare_query. Example: velixo query build \"AR balance per customer as of {{AsOfDate}}\" --intent \"...\" > ar.plan.json")
        {
            request,
            baseQuery,
        };
        command.SetAction(_ => NotImplemented("velixo query build"));
        return command;
    }

    private static Command BuildRun()
    {
        var plan = new Option<string?>("--plan") { Description = "Run every item of a saved plan.", HelpName = "file" };
        var query = new Option<string?>("--query") { Description = "Run one query from a file, or - for stdin.", HelpName = "file|-" };
        var inquiry = new Option<string?>("--inquiry") { Description = "Run a saved generic inquiry as saved.", HelpName = "name" };
        var param = new Option<string[]>("--param")
        {
            Description = "A parameter by its display label: \"As Of Date=2026-09-30\". Repeatable.",
            HelpName = "label=value",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var parameters = new Option<string?>("--params") { Description = "The parameters as a JSON object in a file.", HelpName = "file" };
        var top = new Option<int?>("--top") { Description = "Rows per call; 250 by default, 10,000 at most.", HelpName = "n" };
        var skip = new Option<int?>("--skip") { Description = "Rows to skip before the window.", HelpName = "n" };
        var all = new Option<bool>("--all") { Description = "Page through every window, up to --max-rows." };
        var maxRows = new Option<int?>("--max-rows") { Description = "The most rows --all collects; 10,000 by default.", HelpName = "n" };

        var command = new Command(
            "run",
            "Run a query, a plan or a saved inquiry with execute_query. Example: velixo query run --plan ar.plan.json --param \"As Of Date=2026-09-30\" --intent \"...\"")
        {
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
        command.SetAction(_ => NotImplemented("velixo query run"));
        return command;
    }

    private static Command BuildWorkbook()
    {
        var query = new Option<string?>("--query") { Description = "The query file.", HelpName = "file" };
        var plan = new Option<string?>("--plan") { Description = "A saved plan.", HelpName = "file" };
        var item = new Option<int?>("--item") { Description = "Which item of the plan, from 1.", HelpName = "n" };
        var param = new Option<string[]>("--param")
        {
            Description = "A parameter by its display label. Repeatable.",
            HelpName = "label=value",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var download = new Option<string?>("--download") { Description = "Save the workbook to this path.", HelpName = "path" };

        var command = new Command(
            "workbook",
            "Create a Velixo workbook that holds the query as a live formula, with create_workbook_from_query.")
        {
            query,
            plan,
            item,
            param,
            download,
        };
        command.SetAction(_ => NotImplemented("velixo query workbook"));
        return command;
    }

    private static int NotImplemented(string path) => throw CliException.NotImplemented(path);
}
