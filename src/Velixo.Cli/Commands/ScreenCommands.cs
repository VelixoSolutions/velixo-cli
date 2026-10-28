using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Files;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Screen;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo command build|run</c>: <c>prepare_commands</c> and <c>execute_commands</c>, with the
/// confirmation, the duplicate guard and the continuations of the spec, sections 8.4 and 9.
/// </summary>
public static class ScreenCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new Command("command", "Build and run batches of ERP commands: reads, reports and writes through the ERP's screens.")
        {
            BuildBuild(services),
            BuildRun(services),
            LibraryCommands.BuildList(services, LibraryKind.Screen),
            LibraryCommands.BuildDelete(services, LibraryKind.Screen),
        };
    }

    private static Command BuildBuild(CliServices services)
    {
        var request = new Argument<string>("request")
        {
            Description = "The operation in the user's words, every value as a {{Placeholder}}.",
        };
        var target = new Option<string?>("--target") { Description = "The screen id an earlier build returned, to rebuild or refine that batch.", HelpName = "screen" };
        var name = LibraryCommands.NameOption(LibraryKind.Screen);
        var save = new Option<string?>("--save")
        {
            Description = "Also write the batch to this file, for 'velixo command run --batch FILE'. Replaces a file already there.",
            HelpName = "file",
        };

        var command = new Command(
            "build",
            "Build a reusable batch of screen commands from a request. Example: velixo command build \"Create a sales order for {{CustomerID}}\" --save order.batch.json")
        {
            request,
            target,
            name,
            save,
        };
        command.SetAction((parse, cancellationToken) =>
            BuildBatchAsync(
                services,
                parse.GetValue(request)!,
                parse.GetValue(target),
                parse.GetValue(save),
                LibraryCommands.CheckName(parse.GetValue(name)),
                parse.GetValue(services.Options.Connection),
                cancellationToken));
        return command;
    }

    private static Command BuildRun(CliServices services)
    {
        var number = LibraryCommands.NumberArgument(LibraryKind.Screen, "run");
        var batch = new Option<string?>("--batch")
        {
            Description = "A batch 'command build' wrote to a file, or - to read it from stdin. A batch from stdin needs --yes to write.",
            HelpName = "file|-",
        };
        var variable = new Option<string[]>("--param")
        {
            Description = "A value for a placeholder: NAME=VALUE. An empty value writes a blank. Repeatable; wins over --params.",
            HelpName = "name=value",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var variableNull = new Option<string[]>("--param-null")
        {
            Description = "Pass an explicit null, so the ERP defaults the field. Repeatable.",
            HelpName = "name",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var variables = new Option<string?>("--params") { Description = "The values as a JSON object in a file.", HelpName = "file" };
        var rows = new Option<string?>("--rows") { Description = "The detail rows as a JSON array in a file.", HelpName = "file" };
        var attach = new Option<string[]>("--attach")
        {
            Description = "Stage a file and attach it to the record: PATH or PATH:COMMENT. Repeatable.",
            HelpName = "path[:comment]",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var attachToken = new Option<string[]>("--attach-token")
        {
            Description = "Attach a file 'velixo files stage' staged: TOKEN or TOKEN:COMMENT. Repeatable.",
            HelpName = "token[:comment]",
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
            "Run a built batch by its number, or from a file. At a terminal, asks for each value and line not given. Writes need --yes or a terminal confirmation. "
            + "Example: velixo command run 2 --param CustomerID=ACME --yes")
        {
            number,
            batch,
            variable,
            variableNull,
            variables,
            rows,
            attach,
            attachToken,
            leaveOpen,
            continueOpen,
            continuation,
            finish,
            downloadDir,
            dryRun,
            force,
        };
        command.SetAction((parse, cancellationToken) =>
        {
            var path = parse.GetValue(batch);
            var numbered = parse.GetValue(number);
            if (path is not null && numbered is not null)
            {
                throw CliException.Usage("Give a batch number or --batch, not both.");
            }

            if (path is null && numbered is null && !ValuePrompt.CanAsk(services))
            {
                throw CliException.Usage(
                    "Name the batch to run: a number from 'velixo command list', or --batch FILE.",
                    "velixo command list");
            }

            LibraryEntry? entry = path is null
                ? LibraryCommands.Find(services, LibraryKind.Screen, numbered, "run", parse.GetValue(services.Options.Connection))
                : null;
            return RunAsync(parse, path, entry, cancellationToken);
        });
        return command;

        Task<int> RunAsync(ParseResult parse, string? path, LibraryEntry? entry, CancellationToken cancellationToken) => ScreenRunner.RunAsync(
            services,
            new ScreenRunOptions
            {
                BatchPath = path,
                BatchDocument = entry?.Document,
                BatchSource = entry is null ? path ?? "the batch" : $"batch {entry.Number}",
                Variables = parse.GetValue(variable) ?? [],
                NullVariables = parse.GetValue(variableNull) ?? [],
                VariablesFile = parse.GetValue(variables),
                RowsFile = parse.GetValue(rows),
                Attachments = (parse.GetValue(attach) ?? []).Select(value => ParseAttachment(value, services.Environment.CurrentDirectory))
                    .Concat(ParseStagedAttachments(services, parse.GetValue(attachToken) ?? []))
                    .ToList(),
                LeaveOpen = parse.GetValue(leaveOpen),
                Continue = parse.GetValue(continueOpen),
                ContinuationFile = parse.GetValue(continuation),
                Finish = parse.GetValue(finish),
                DownloadDirectory = parse.GetValue(downloadDir),
                DryRun = parse.GetValue(dryRun),
                Force = parse.GetValue(force),
            },
            cancellationToken);
    }

    /// <summary>Reads each <c>--attach-token</c>, <c>TOKEN</c> or <c>TOKEN:COMMENT</c>, and finds the file each token was staged for on this profile.</summary>
    /// <exception cref="CliException">Exit 2 when this profile staged no file under a token.</exception>
    internal static IEnumerable<ScreenAttachment> ParseStagedAttachments(CliServices services, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return [];
        }

        IReadOnlyList<StoredStagedFile> staged = StagedFiles.Load(services);
        return values
            .Select(value =>
            {
                (StoredStagedFile file, string? comment) = StagedFiles.FindWithComment(staged, value);
                return new ScreenAttachment(string.Empty, comment)
                {
                    FileToken = file.FileToken,
                    StagedName = file.FileName,
                    StagedSize = file.SizeBytes,
                };
            })
            .ToList();
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

    /// <summary>Calls <c>prepare_commands</c>, saves the batch when <paramref name="savePath"/> names a file, and prints it.</summary>
    private static async Task<int> BuildBatchAsync(
        CliServices services,
        string request,
        string? target,
        string? savePath,
        string? libraryName,
        string? forcedConnection,
        CancellationToken cancellationToken)
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
        var number = result.Document is JsonObject built && ScreenOutput.IsBatch(built)
            ? LibraryCommands.Save(services, LibraryKind.Screen, built, request, libraryName)
            : null;

        if (!string.IsNullOrEmpty(savePath))
        {
            File.WriteAllText(Path.GetFullPath(savePath, services.Environment.CurrentDirectory), JsonText.Indented(result.Document));
            services.Output.Progress($"Saved the batch to {savePath}.");
        }

        if (result.Document is JsonObject document)
        {
            ScreenOutput.WriteBatch(
                services.Output,
                document,
                string.IsNullOrEmpty(savePath) ? null : savePath,
                forcedConnection,
                InlineInput.Detect(services.Environment),
                number);
        }
        else
        {
            services.Output.WriteResult(result.Document);
        }

        return ExitCodes.Success;
    }
}
