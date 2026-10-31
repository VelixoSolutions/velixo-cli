using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Files;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo files</c>: <c>stage</c> stages a file for a later <c>command run --attach-token</c> or
/// <c>import run --file-token</c>, uploading it or printing the URL that takes its bytes, and
/// <c>download</c> saves the reports, attachments and workbooks behind the links the server issues.
/// A file on this machine needs no staging step: <c>--attach</c> and <c>--file</c> take its path.
/// </summary>
public static class FilesCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new Command("files", "Stage files for the ERP, and download the reports, attachments and workbooks it produced.")
        {
            BuildStage(services),
            BuildDownload(services),
        };
    }

    private static Command BuildStage(CliServices services)
    {
        var file = new Argument<string>("file")
        {
            Description = "A file on this machine, or, with --size, the name of a file you will upload from elsewhere.",
        };
        var size = new Option<long?>("--size")
        {
            Description = "The file's size in bytes, for a file that is not on this machine. The command then prints the upload URL instead of uploading.",
            HelpName = "bytes",
        };
        var sha256 = new Option<string?>("--sha256") { Description = "With --size: the file's SHA-256 in hex, which the server checks against the bytes that arrive.", HelpName = "hex" };
        var noUpload = new Option<bool>("--no-upload") { Description = "Do not upload a file on this machine: print the upload URL and the curl command that sends it." };

        var command = new Command(
            "stage",
            "Stage a file for 'command run --attach-token' or 'import run --file-token', and print its token. "
            + "A file on this machine is uploaded; with --size or --no-upload, the output gives the URL to upload it with curl. "
            + "Example: velixo files stage invoice.pdf")
        {
            file,
            size,
            sha256,
            noUpload,
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var given = parse.GetValue(file)!;
            var declaredSize = parse.GetValue(size);
            var transfer = new FileTransfer(services.Mcp, services.Http);

            string fileName;
            long sizeBytes;
            string? hash;
            string? localPath = null;
            if (declaredSize is { } bytes)
            {
                if (bytes <= 0)
                {
                    throw CliException.Usage("--size must be the file's size in bytes, more than 0.");
                }

                fileName = Path.GetFileName(given);
                if (fileName.Length == 0)
                {
                    throw CliException.Usage($"'{given}' names no file: give the file's name, such as invoice.pdf.");
                }

                sizeBytes = bytes;
                hash = parse.GetValue(sha256)?.Trim().ToLowerInvariant();
                if (hash is not null && (hash.Length != 64 || !hash.All(char.IsAsciiHexDigitLower)))
                {
                    throw CliException.Usage($"--sha256 must be the file's SHA-256 as 64 hexadecimal characters, not '{hash}'.", "shasum -a 256 FILE prints it.");
                }
            }
            else
            {
                if (parse.GetValue(sha256) is not null)
                {
                    throw CliException.Usage("--sha256 goes with --size; for a file on this machine the CLI computes it.");
                }

                localPath = Path.GetFullPath(given, services.Environment.CurrentDirectory);
                hash = await FileTransfer.HashFileAsync(localPath, cancellationToken).ConfigureAwait(false);
                fileName = Path.GetFileName(localPath);
                sizeBytes = new FileInfo(localPath).Length;
            }

            services.Output.Progress($"staging {fileName}");
            UploadLink link = await transfer.RequestLinkAsync(fileName, sizeBytes, hash, cancellationToken).ConfigureAwait(false);
            var upload = localPath is not null && !parse.GetValue(noUpload);
            if (upload)
            {
                await transfer.UploadAsync(localPath!, link.UploadUrl, cancellationToken).ConfigureAwait(false);
            }

            StagedFiles.Remember(services, new StoredStagedFile { FileToken = link.FileToken, FileName = fileName, SizeBytes = sizeBytes, ExpiresAt = link.ExpiresAt });

            var document = new JsonObject
            {
                ["fileName"] = fileName,
                ["sizeBytes"] = sizeBytes,
                ["fileToken"] = link.FileToken,
                ["expiresAt"] = link.ExpiresAt,
                ["uploaded"] = upload,
            };
            if (!upload)
            {
                // The URL takes the bytes once, and is a secret bound to the signed-in user.
                document["uploadUrl"] = link.UploadUrl;
                document["curl"] = InlineInput.CurlUpload(InlineInput.Detect(services.Environment), localPath is null ? fileName : given, link.UploadUrl);
            }

            services.Output.WriteResult(document);
            return ExitCodes.Success;
        });

        return command;
    }

    private static Command BuildDownload(CliServices services)
    {
        var urls = new Argument<string[]>("url") { Description = "Download links the server issued.", Arity = ArgumentArity.ZeroOrMore };
        var output = new Option<string?>("--out") { Description = "Save the one file to this path.", HelpName = "path" };
        var directory = new Option<string?>("--dir") { Description = "Save the files into this directory.", HelpName = "dir" };
        var from = new Option<string?>("--from") { Description = "Download every attachment of a saved 'command run' output.", HelpName = "result.json" };

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
                DownloadedFile file = await transfer.DownloadAsync(link, directoryPath, outPath, fileName: null, cancellationToken).ConfigureAwait(false);
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
                : CliException.Usage($"'{resultPath}' has no attachments with a downloadUrl.", "Download links come from a 'command run' output that rendered a report or read a record's files.");
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
