using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Files;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo files upload|request|staged|download</c>: <c>get_upload_url</c>, <c>list_staged_files</c>
/// and the download links the server issues.
/// </summary>
public static class FilesCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new Command("files", "Stage files for the ERP and download what it produced.")
        {
            BuildUpload(services),
            BuildRequest(services),
            BuildStaged(services),
            BuildDownload(services),
        };
    }

    private static Command BuildUpload(CliServices services)
    {
        var paths = new Argument<string[]>("path") { Description = "The files to stage.", Arity = ArgumentArity.OneOrMore };
        var comment = new Option<string?>("--comment") { Description = "A comment kept with each file.", HelpName = "text" };

        var command = new Command("upload", "Stage files and print a fileToken for each. Example: velixo files upload invoice.pdf") { paths, comment };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var transfer = new FileTransfer(services.Mcp, services.Http);
            var staged = new JsonArray();

            foreach (var path in parse.GetValue(paths)!)
            {
                services.Output.Progress($"staging {Path.GetFileName(path)}");
                StagedFile file = await transfer
                    .StageAsync(Path.GetFullPath(path, services.Environment.CurrentDirectory), parse.GetValue(comment), cancellationToken)
                    .ConfigureAwait(false);
                staged.Add((JsonNode)file.ToJson());
            }

            services.Output.WriteResult(staged);
            return ExitCodes.Success;
        });
        return command;
    }

    private static Command BuildRequest(CliServices services)
    {
        var maxFiles = new Option<int?>("--max-files") { Description = "How many files the page accepts.", HelpName = "n" };

        var command = new Command("request", "Create an upload page for the user to add files to.") { maxFiles };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var arguments = new JsonObject();
            if (parse.GetValue(maxFiles) is { } count)
            {
                arguments["maxFiles"] = count >= 1 ? count : throw CliException.Usage("--max-files must be at least 1.");
            }

            McpToolResult called = await services.Mcp.CallToolAsync(McpToolNames.GetUploadUrl, arguments, cancellationToken).ConfigureAwait(false);
            UnwrappedToolResult page = ToolResultUnwrapper.Unwrap(called);
            CommandText.RelayWarnings(services.Output, page.Document);
            services.Output.WriteResult(page.Data ?? page.Document);
            return ExitCodes.Success;
        });
        return command;
    }

    private static Command BuildStaged(CliServices services)
    {
        var batchToken = new Argument<string>("batch-token") { Description = "The uploadBatchToken 'files request' printed." };
        var wait = new Option<bool>("--wait") { Description = "Wait until the user sends the files." };

        var command = new Command("staged", "List the files the user staged on an upload page.") { batchToken, wait };
        command.SetAction(async (parse, cancellationToken) =>
        {
            UnwrappedToolResult staged = await ReadStagedAsync(services, parse.GetValue(batchToken)!, parse.GetValue(wait), cancellationToken).ConfigureAwait(false);
            CommandText.RelayWarnings(services.Output, staged.Document);
            if (CommandText.String((staged.Data as JsonObject)?["note"]) is { Length: > 0 } note)
            {
                services.Output.Warn(note);
            }

            services.Output.WriteResult(staged.Data ?? staged.Document);
            return ExitCodes.Success;
        });
        return command;
    }

    /// <summary>
    /// Reads the upload page's files once, or with <paramref name="wait"/> repeats the server's hold
    /// until the user has pressed Send or the server says the waiting has ended with a <c>note</c>.
    /// </summary>
    private static async Task<UnwrappedToolResult> ReadStagedAsync(CliServices services, string batchToken, bool wait, CancellationToken cancellationToken)
    {
        TimeSpan? timeout = services.Settings.Timeout;
        using CancellationTokenSource deadline = timeout is { } limit ? new CancellationTokenSource(limit, services.Clock) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            while (true)
            {
                var arguments = new JsonObject { ["uploadBatchToken"] = batchToken, ["waitForSubmit"] = wait };
                McpToolResult called = await services.Mcp.CallToolAsync(McpToolNames.ListStagedFiles, arguments, linked.Token).ConfigureAwait(false);
                UnwrappedToolResult listing = ToolResultUnwrapper.Unwrap(called);

                var data = listing.Data as JsonObject;
                if (!wait || IsFinal(data))
                {
                    return listing;
                }

                var files = data?["files"] is JsonArray received ? received.Count : 0;
                var capacity = data?["maxFiles"] is JsonValue max && max.TryGetValue(out int slots) ? $" of {slots}" : string.Empty;
                services.Output.Progress($"waiting for the user to press Send, {files}{capacity} files so far");
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new CliException(
                ExitCodes.Timeout,
                ErrorCodes.Timeout,
                $"The user had not pressed Send after {DurationText.Format(timeout ?? TimeSpan.Zero)}.",
                $"velixo files staged {batchToken} --wait");
        }
    }

    private static bool IsFinal(JsonObject? listing) =>
        (listing?["submitted"] is JsonValue submitted && submitted.TryGetValue(out bool sent) && sent)
        || CommandText.String(listing?["note"]) is { Length: > 0 };

    private static Command BuildDownload(CliServices services)
    {
        var urls = new Argument<string[]>("url") { Description = "Download links the server issued.", Arity = ArgumentArity.ZeroOrMore };
        var output = new Option<string?>("--out") { Description = "Save the one file to this path.", HelpName = "path" };
        var directory = new Option<string?>("--dir") { Description = "Save the files into this directory.", HelpName = "dir" };
        var from = new Option<string?>("--from") { Description = "Download every attachment of a saved 'screen run' output.", HelpName = "result.json" };

        var command = new Command("download", "Download reports, attachments and workbooks by their links.") { urls, output, directory, from };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var outPath = parse.GetValue(output);
            var directoryPath = parse.GetValue(directory);
            var resultFile = parse.GetValue(from);
            List<string> links = CollectLinks(parse.GetValue(urls)!, resultFile is null ? null : Path.GetFullPath(resultFile, services.Environment.CurrentDirectory));

            if (outPath is not null && directoryPath is not null)
            {
                throw CliException.Usage("Give --out or --dir, not both.");
            }

            if (outPath is not null && links.Count != 1)
            {
                throw CliException.Usage($"--out names one file, and {links.Count} links were given.", "Use --dir to save several files.");
            }

            var workingDirectory = services.Environment.CurrentDirectory;
            outPath = outPath is null ? null : Path.GetFullPath(outPath, workingDirectory);
            directoryPath = outPath is null ? Path.GetFullPath(directoryPath ?? workingDirectory, workingDirectory) : null;

            var transfer = new FileTransfer(services.Mcp, services.Http);
            var saved = new JsonArray();
            foreach (var link in links)
            {
                DownloadedFile file = await transfer.DownloadAsync(link, directoryPath, outPath, cancellationToken).ConfigureAwait(false);
                saved.Add((JsonNode)file.ToJson());
            }

            services.Output.WriteResult(saved);
            return ExitCodes.Success;
        });
        return command;
    }

    /// <summary>The links given on the command line, then the ones in the saved output, without repeats.</summary>
    private static List<string> CollectLinks(string[] given, string? resultPath)
    {
        var links = new List<string>(given);
        if (resultPath is not null)
        {
            links.AddRange(AttachmentLinks.Find(ReadDocument(resultPath)));
        }

        if (links.Count == 0)
        {
            throw resultPath is null
                ? CliException.Usage("Give at least one download link, or --from a saved result.", "velixo files download --help")
                : CliException.Usage($"'{resultPath}' has no attachments with a downloadUrl.", "Download links come from a 'screen run' output that rendered a report or read a record's files.");
        }

        return links.Distinct(StringComparer.Ordinal).ToList();
    }

    private static JsonNode? ReadDocument(string path)
    {
        if (!File.Exists(path))
        {
            throw CliException.Usage($"The result file '{path}' does not exist.");
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw CliException.Usage($"The result file '{path}' is not valid JSON: {exception.Message}");
        }
    }
}
