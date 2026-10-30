using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
public static partial class ScreenCommands
{
    private const string AttachOption = "--attach";

    private const string AttachTokenOption = "--attach-token";

    private const string AttachCommentOption = "--attach-comment";

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
        var acceptDefaults = new Option<bool>("--accept-defaults")
        {
            Description = "Let the ERP fill every field the request does not name, as the screen would. Pass it when the user said to use the defaults.",
        };

        var command = new Command(
            "build",
            "Build a reusable batch of screen commands from a request. Example: velixo command build \"Create a sales order for {{CustomerID}}\" --save order.batch.json")
        {
            request,
            target,
            name,
            save,
            acceptDefaults,
        };
        command.SetAction((parse, cancellationToken) =>
            BuildBatchAsync(
                services,
                parse.GetValue(request)!,
                parse.GetValue(acceptDefaults),
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
        var attach = new Option<string[]>(AttachOption)
        {
            Description = "Upload a file from this path and attach it to the record. Repeatable, one file each.",
            HelpName = "path",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var attachToken = new Option<string[]>(AttachTokenOption)
        {
            Description = "Attach a file 'velixo files stage' staged, by its token. Repeatable.",
            HelpName = "token",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var attachComment = new Option<string[]>(AttachCommentOption)
        {
            Description = "The comment stored with the file of the --attach or --attach-token just before it.",
            HelpName = "text",
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
            attachComment,
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
                Attachments = ReadAttachments(services, parse),
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

    /// <summary>
    /// Reads the files to attach in the order the command line gives them: each <c>--attach PATH</c> or
    /// <c>--attach-token TOKEN</c> is one file, and an <c>--attach-comment TEXT</c> right after it is that
    /// file's comment. A token is looked up among the files this profile staged, for the name it was issued for.
    /// </summary>
    /// <exception cref="CliException">
    /// Exit 2 for a file that does not exist, a token this profile did not stage, a comment with no file
    /// before it, or a second comment for one file.
    /// </exception>
    internal static List<ScreenAttachment> ReadAttachments(CliServices services, ParseResult parse)
    {
        var attachments = new List<ScreenAttachment>();
        var commented = new HashSet<int>();
        IReadOnlyList<StoredStagedFile>? staged = null;
        IReadOnlyList<Token> tokens = parse.Tokens;

        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            if (tokens[index].Type != TokenType.Option)
            {
                continue;
            }

            var value = tokens[index + 1].Value;
            switch (tokens[index].Value)
            {
                case AttachOption:
                    attachments.Add(LocalAttachment(value, services.Environment.CurrentDirectory));
                    break;
                case AttachTokenOption:
                    StoredStagedFile file = StagedFiles.Find(staged ??= StagedFiles.Load(services), value.Trim());
                    attachments.Add(new ScreenAttachment(string.Empty, Comment: null)
                    {
                        FileToken = file.FileToken,
                        StagedName = file.FileName,
                        StagedSize = file.SizeBytes,
                    });
                    break;
                case AttachCommentOption:
                    if (attachments.Count == 0)
                    {
                        throw CliException.Usage(
                            "--attach-comment names no file: put it right after the --attach or --attach-token it describes.",
                            "velixo command run NUMBER --attach invoice.pdf --attach-comment \"Vendor invoice\"");
                    }

                    if (!commented.Add(attachments.Count - 1))
                    {
                        throw CliException.Usage($"The file '{attachments[^1].FileName}' has two --attach-comment; give it one.");
                    }

                    attachments[^1] = attachments[^1] with { Comment = string.IsNullOrWhiteSpace(value) ? null : value.Trim() };
                    break;
            }
        }

        return attachments;
    }

    /// <summary>Reads one <c>--attach</c>: the path of a file that exists, relative to <paramref name="currentDirectory"/> or absolute.</summary>
    /// <exception cref="CliException">Exit 2 when the file does not exist, so nothing is confirmed or staged for a typo.</exception>
    internal static ScreenAttachment LocalAttachment(string path, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(path);

        var fullPath = Path.GetFullPath(path, currentDirectory);
        return File.Exists(fullPath)
            ? new ScreenAttachment(fullPath, Comment: null)
            : throw CliException.Usage($"The file to attach, '{path}', does not exist.", "Pass the path of a file the user gave you.");
    }

    /// <summary>The sentence <c>--accept-defaults</c> adds to the request. The builder reads it as the user's leave to default every field the request does not name, including a branch or a project, which it otherwise asks for.</summary>
    internal const string AcceptDefaultsSentence = "Leave every field this request does not name at its default.";

    internal static string WithDefaultsAccepted(string request)
    {
        var trimmed = request.Trim();
        if (trimmed.Length == 0)
        {
            return AcceptDefaultsSentence;
        }

        var lastWord = trimmed.TrimEnd('"', '\'', ')', ']', '\u201D', '\u2019');
        var separator = lastWord.Length > 0 && lastWord[^1] is '.' or '!' or '?' ? " " : ". ";
        return trimmed + separator + AcceptDefaultsSentence;
    }

    /// <summary>
    /// Whether a builder refusal offers to leave the unnamed fields to the ERP: "authorize their defaults",
    /// "authorize leaving every other field at its default". A refusal that only mentions a default, or an
    /// authorization, asks something else.
    /// </summary>
    internal static bool OffersDefaults(string refusal) => DefaultsOffer().IsMatch(refusal);

    [GeneratedRegex(@"\bauthori[sz]e\s+(?:their|the|its)\s+defaults?\b|\bauthori[sz]e\s+leaving\b", RegexOptions.IgnoreCase)]
    private static partial Regex DefaultsOffer();

    [GeneratedRegex(@"\(target for the rebuild:\s*([^)\s]+)\s*\)")]
    private static partial Regex RebuildTarget();

    /// <summary>The refusal again, with a next step that offers both answers: the values, or --accept-defaults.</summary>
    private static CliException WithDefaultsNextStep(CliException refusal)
    {
        var nextStep =
            "The builder asks for values the request does not name, or the user's leave to let the ERP fill them. "
            + "Ask the user which. To give the values, build again with them written into the request as placeholders; "
            + "to use the defaults, build again with the same request and --accept-defaults.";
        if (RebuildTarget().Match(refusal.Message) is { Success: true } match)
        {
            nextStep += $" Either way, add --target {match.Groups[1].Value}.";
        }

        return new CliException(refusal.ExitCode, refusal.Code, refusal.Message, nextStep, refusal)
        {
            TaskId = refusal.TaskId,
            Detail = refusal.Detail,
        };
    }

    /// <summary>Calls <c>prepare_commands</c>, saves the batch when <paramref name="savePath"/> names a file, and prints it.</summary>
    private static async Task<int> BuildBatchAsync(
        CliServices services,
        string request,
        bool acceptDefaults,
        string? target,
        string? savePath,
        string? libraryName,
        string? forcedConnection,
        CancellationToken cancellationToken)
    {
        var arguments = new JsonObject
        {
            [McpToolNames.IntentArgument] = services.IntentOrDefault(),
            ["request"] = acceptDefaults ? WithDefaultsAccepted(request) : request,
        };

        if (services.Settings.Connection is { } connection)
        {
            arguments["connectionName"] = connection;
        }

        if (target is not null)
        {
            arguments["target"] = target;
        }

        UnwrappedToolResult result;
        try
        {
            result = await services.Tasks
                .RunAsync(token => services.Mcp.CallToolAsync(McpToolNames.PrepareCommands, arguments, token), services.FollowOptions(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CliException refusal) when (refusal.Code == ErrorCodes.BuilderRefusal && !acceptDefaults && OffersDefaults(refusal.Message))
        {
            throw WithDefaultsNextStep(refusal);
        }

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
