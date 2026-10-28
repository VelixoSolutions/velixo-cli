using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Screen;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo screen build|run</c>: <c>prepare_commands</c> and <c>execute_commands</c>, with the
/// confirmation, the duplicate guard and the continuations of the spec, sections 8.4 and 9.
/// </summary>
public static class ScreenCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new Command("screen", "Build and run batches of ERP screen commands.")
        {
            BuildBuild(services),
            BuildRun(services),
        };
    }

    private static Command BuildBuild(CliServices services)
    {
        var request = new Argument<string>("request")
        {
            Description = "The operation in the user's words, every value as a {{Placeholder}}.",
        };
        var target = new Option<string?>("--target") { Description = "The screen id an earlier build returned, to rebuild or refine that batch.", HelpName = "screen" };

        var command = new Command(
            "build",
            "Build a reusable batch with prepare_commands. Example: velixo screen build \"Create a sales order for {{CustomerID}}\" --intent \"...\" > order.batch.json")
        {
            request,
            target,
        };
        command.SetAction((parse, cancellationToken) =>
            BuildBatchAsync(services, parse.GetValue(request)!, parse.GetValue(target), cancellationToken));
        return command;
    }

    private static Command BuildRun(CliServices services)
    {
        var batch = new Option<string>("--batch") { Description = "The batch file 'screen build' wrote.", HelpName = "file", Required = true };
        var variable = new Option<string[]>("--var")
        {
            Description = "Bind a placeholder: NAME=VALUE. An empty value writes a blank. Repeatable.",
            HelpName = "name=value",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var variableNull = new Option<string[]>("--var-null")
        {
            Description = "Pass an explicit null, so the ERP defaults the field. Repeatable.",
            HelpName = "name",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var variables = new Option<string?>("--vars") { Description = "The variables as a JSON object in a file.", HelpName = "file" };
        var rows = new Option<string?>("--rows") { Description = "The detail rows as a JSON array in a file.", HelpName = "file" };
        var attach = new Option<string[]>("--attach")
        {
            Description = "Stage a file and attach it to the record: PATH or PATH:COMMENT. Repeatable.",
            HelpName = "path[:comment]",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var leaveOpen = new Option<bool>("--leave-open") { Description = "Leave the record open for a later --continue." };
        var continueOpen = new Option<bool>("--continue") { Description = "Continue the record the last --leave-open left open." };
        var continuation = new Option<string?>("--continuation") { Description = "Continue the record a saved continuation names.", HelpName = "file" };
        var finish = new Option<bool>("--finish") { Description = "Run the batch's finish commands on the open record." };
        var downloadDir = new Option<string?>("--download-dir") { Description = "Save every report and attachment the batch produced here.", HelpName = "dir" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Send nothing: bind the variables, report what is missing, print the summary." };
        var force = new Option<bool>("--force") { Description = "Run a batch the duplicate guard would refuse." };

        var command = new Command(
            "run",
            "Run a batch with execute_commands. Writes need --yes or a terminal confirmation. Example: velixo screen run --batch order.batch.json --var CustomerID=ACME --yes --intent \"...\"")
        {
            batch,
            variable,
            variableNull,
            variables,
            rows,
            attach,
            leaveOpen,
            continueOpen,
            continuation,
            finish,
            downloadDir,
            dryRun,
            force,
        };
        command.SetAction((parse, cancellationToken) => ScreenRunner.RunAsync(
            services,
            new ScreenRunOptions
            {
                BatchPath = parse.GetValue(batch)!,
                Variables = parse.GetValue(variable) ?? [],
                NullVariables = parse.GetValue(variableNull) ?? [],
                VariablesFile = parse.GetValue(variables),
                RowsFile = parse.GetValue(rows),
                Attachments = (parse.GetValue(attach) ?? []).Select(value => ParseAttachment(value, services.Environment.CurrentDirectory)).ToList(),
                LeaveOpen = parse.GetValue(leaveOpen),
                Continue = parse.GetValue(continueOpen),
                ContinuationFile = parse.GetValue(continuation),
                Finish = parse.GetValue(finish),
                DownloadDirectory = parse.GetValue(downloadDir),
                DryRun = parse.GetValue(dryRun),
                Force = parse.GetValue(force),
            },
            cancellationToken));
        return command;
    }

    /// <summary>
    /// Reads one <c>--attach</c>, <c>PATH</c> or <c>PATH:COMMENT</c>, resolving a relative path against
    /// <paramref name="currentDirectory"/>. The value splits on its last colon, except when that colon
    /// belongs to the path: a drive letter (<c>C:\x.pdf</c>), a path separator right after it, or a file
    /// whose own name holds the colon.
    /// </summary>
    /// <exception cref="CliException">Exit 2 when the file does not exist, so nothing is confirmed or staged for a typo.</exception>
    internal static ScreenAttachment ParseAttachment(string value, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(value);

        var path = value;
        string? comment = null;

        var colon = value.LastIndexOf(':');
        if (colon > 0 && !File.Exists(Path.GetFullPath(value, currentDirectory)))
        {
            var before = value[..colon];
            var after = value[(colon + 1)..];
            var isDrive = before.Length == 1 && char.IsAsciiLetter(before[0]);
            var isPath = after.Length > 0 && after[0] is '/' or '\\';
            if (!isDrive && !isPath)
            {
                path = before;
                comment = after.Length == 0 ? null : after;
            }
        }

        var fullPath = Path.GetFullPath(path, currentDirectory);
        return File.Exists(fullPath)
            ? new ScreenAttachment(fullPath, comment)
            : throw CliException.Usage(
                $"The file to attach, '{path}', does not exist.",
                "Pass the path of a file the user gave you, optionally followed by :COMMENT.");
    }

    /// <summary>Calls <c>prepare_commands</c> and prints the batch.</summary>
    private static async Task<int> BuildBatchAsync(CliServices services, string request, string? target, CancellationToken cancellationToken)
    {
        var arguments = new JsonObject
        {
            [McpToolNames.IntentArgument] = services.IntentOrDefault(),
            ["request"] = request,
        };

        if (services.Settings.Connection is { } connection)
        {
            arguments["connectionName"] = connection;
        }

        if (target is not null)
        {
            arguments["target"] = target;
        }

        UnwrappedToolResult result = await services.Tasks
            .RunAsync(token => services.Mcp.CallToolAsync(McpToolNames.PrepareCommands, arguments, token), services.FollowOptions(), cancellationToken)
            .ConfigureAwait(false);

        CommandText.RelayWarnings(services.Output, result.Document);
        services.Output.WriteResult(result.Document);
        return ExitCodes.Success;
    }
}
