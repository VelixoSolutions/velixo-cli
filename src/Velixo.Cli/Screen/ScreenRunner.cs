using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Config;
using Velixo.Cli.Files;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>
/// <c>velixo command run</c>: checks the batch locally, confirms a write, refuses a duplicate, stages
/// the attachments, calls <c>execute_commands</c>, and keeps the record a <c>--leave-open</c> call
/// left open for the next <c>--continue</c>.
/// </summary>
internal static class ScreenRunner
{
    public static async Task<int> RunAsync(CliServices services, ScreenRunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var intent = services.IntentOrDefault();
        if (options.Continue && options.ContinuationFile is not null)
        {
            throw CliException.Usage("Give --continue or --continuation FILE, not both.");
        }

        var directory = services.Environment.CurrentDirectory;
        string FullPath(string path) => Path.GetFullPath(path, directory);

        ScreenBatch batch = options switch
        {
            { BatchDocument: { } document } => ScreenBatch.From(document, options.BatchSource),
            { BatchPath: "-" } => ScreenBatch.Parse(services.Console.In.ReadToEnd(), "the batch on stdin", "stdin"),
            _ => ScreenBatch.Read(FullPath(options.BatchPath!)),
        };
        JsonObject variables = ScreenVariables.Bind(
            options.VariablesFile is null ? null : FullPath(options.VariablesFile),
            options.Variables,
            options.NullVariables);
        JsonArray? rows = options.RowsFile is null ? null : ScreenRows.Read(FullPath(options.RowsFile));

        // A person at a terminal is asked for what the flags did not give. A continued record already
        // has its header, so only a first call asks; a batch read from stdin has used up the terminal.
        if (ValuePrompt.CanAsk(services) && options.BatchPath != "-" && !options.Continue && options.ContinuationFile is null)
        {
            ScreenPrompts.AskForValues(services, batch, variables);
            if (batch.LineCommands is not null && rows is null)
            {
                rows = ScreenPrompts.AskForRows(services, batch, variables);
            }
        }

        var profile = services.Settings.ProfileName;
        ProfileState state = services.Profiles.LoadState(profile);
        ScreenContinuationSource? continuation = options switch
        {
            { Continue: true } => ScreenContinuations.LoadStored(state),
            { ContinuationFile: { } file } => ScreenContinuations.ReadFile(FullPath(file), state),
            _ => null,
        };

        var plan = ScreenRunPlan.Create(batch, variables, rows, continuation, options.LeaveOpen, options.Finish, options.Attachments);
        plan.CheckBindings();

        if (continuation is not null)
        {
            ScreenContinuations.Check(continuation, CurrentSessionId(services, state), batch.Target, services.Clock.GetUtcNow());
        }

        var connection = services.Settings.Connection;
        if (options.DryRun)
        {
            foreach (var line in plan.Summary(connection))
            {
                services.Output.Progress(line);
            }

            services.Output.WriteResult(plan.DryRunDocument(connection));
            return ExitCodes.Success;
        }

        string? fingerprint = null;
        if (plan.CanWrite)
        {
            fingerprint = ScreenDuplicateGuard.Fingerprint(plan.Fingerprinted(connection));
            ScreenDuplicateGuard.Check(state, fingerprint, services.Clock.GetUtcNow(), options.Force);
            Confirmation.Require(
                services,
                plan.Summary(connection),
                question: "Run this batch?",
                notAsked: "This batch can write to the ERP, and nothing confirmed it: the CLI cannot ask without a terminal. The summary is on stderr.",
                notAskedNextStep: "Tell the user in business terms what the batch will do and ask them; once they agree, run the same command again with --yes.",
                declined: "The batch did not run: the confirmation was declined.");
        }

        JsonArray? staged = options.Attachments.Count > 0
            ? await StageAsync(services, options.Attachments, cancellationToken).ConfigureAwait(false)
            : null;
        JsonObject arguments = plan.ToArguments(intent, connection, staged);

        UnwrappedToolResult result;
        try
        {
            result = await services.Tasks
                .RunAsync(token => services.Mcp.CallToolAsync(McpToolNames.ExecuteCommands, arguments, token), services.FollowOptions(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CliException error)
        {
            AfterFailure(services, profile, fingerprint, MightHaveRun(error.ExitCode), EndsContinuation(error));
            throw ScreenErrors.Refine(error);
        }
        catch (OperationCanceledException)
        {
            AfterFailure(services, profile, fingerprint, mightHaveRun: true, endsContinuation: true);
            throw;
        }

        AfterSuccess(services, profile, fingerprint, result);

        if (options.DownloadDirectory is { } downloads && result.Document["result"]?["attachments"] is JsonArray files)
        {
            await DownloadAsync(services, files, FullPath(downloads), cancellationToken).ConfigureAwait(false);
        }

        CommandText.RelayWarnings(services.Output, result.Document);
        services.Output.WriteResult(result.Document);
        return ExitCodes.Success;
    }

    /// <summary>Stages each <c>--attach</c> file and returns the <c>attachments</c> argument of <c>execute_commands</c>.</summary>
    private static async Task<JsonArray> StageAsync(CliServices services, IReadOnlyList<ScreenAttachment> files, CancellationToken cancellationToken)
    {
        var transfer = new FileTransfer(services.Mcp, services.Http);
        var attachments = new JsonArray();
        foreach (ScreenAttachment file in files)
        {
            JsonObject attachment;
            if (file.FileToken is { } token)
            {
                attachment = new JsonObject { ["fileName"] = file.FileName, ["fileToken"] = token };
            }
            else
            {
                services.Output.Progress($"staging {file.FileName}");
                StagedFile staged = await transfer.StageAsync(file.Path, file.Comment, cancellationToken).ConfigureAwait(false);
                attachment = new JsonObject { ["fileName"] = staged.FileName, ["fileToken"] = staged.FileToken };
            }

            if (file.Comment is not null)
            {
                attachment["comment"] = file.Comment;
            }

            attachments.Add((JsonNode)attachment);
        }

        return attachments;
    }

    /// <summary>
    /// Saves every <c>result.attachments[]</c> entry that carries a <c>downloadUrl</c> into
    /// <paramref name="directory"/> under the entry's <c>fileName</c>, and adds the saved <c>path</c> to the entry.
    /// </summary>
    /// <exception cref="CliException">
    /// The download's own exit code and code, with a <c>nextStep</c> that says not to run the batch again,
    /// because the batch already ran.
    /// </exception>
    private static async Task DownloadAsync(CliServices services, JsonArray attachments, string directory, CancellationToken cancellationToken)
    {
        var transfer = new FileTransfer(services.Mcp, services.Http);
        foreach (JsonObject entry in attachments.OfType<JsonObject>())
        {
            if (CommandText.String(entry["downloadUrl"]) is not { Length: > 0 } url)
            {
                continue;
            }

            var fileName = CommandText.String(entry["fileName"]);
            services.Output.Progress($"downloading {fileName ?? "an attachment"}");
            try
            {
                DownloadedFile saved = await transfer.DownloadAsync(url, directory, outPath: null, fileName, cancellationToken).ConfigureAwait(false);
                entry["path"] = saved.Path;
            }
            catch (CliException error)
            {
                throw new CliException(
                    error.ExitCode,
                    error.Code,
                    $"The batch ran, but a download failed: {error.Message}",
                    "Do not run the batch again. Run a read-only batch that delivers the file again, or open the record in the ERP.",
                    error);
            }
        }
    }

    /// <summary>
    /// Returns the MCP session the next call goes out in: the client's, or before its first request
    /// the one stored for this server, which is the one the client will send.
    /// </summary>
    private static string? CurrentSessionId(CliServices services, ProfileState state) =>
        services.Mcp.SessionId
        ?? (string.Equals(state.McpUrl, services.Settings.McpUrl.ToString(), StringComparison.Ordinal) ? state.McpSessionId : null);

    /// <summary>A call that failed in one of these ways may have written, so the duplicate guard counts it.</summary>
    private static bool MightHaveRun(int exitCode) =>
        exitCode is ExitCodes.TaskFailed or ExitCodes.Timeout or ExitCodes.Interrupted or ExitCodes.Failure;

    private static bool EndsContinuation(CliException error) =>
        ScreenErrors.EndsContinuation(error) || error.ExitCode is ExitCodes.Timeout or ExitCodes.Interrupted;

    private static void AfterFailure(CliServices services, string profile, string? fingerprint, bool mightHaveRun, bool endsContinuation)
    {
        var record = fingerprint is not null && mightHaveRun;
        if (!record && !endsContinuation)
        {
            return;
        }

        services.Profiles.UpdateState(profile, state =>
        {
            if (record)
            {
                ScreenDuplicateGuard.Record(state, fingerprint!, services.Clock.GetUtcNow());
            }

            if (endsContinuation)
            {
                state.LastContinuation = null;
            }
        });
    }

    /// <summary>
    /// Records the write and keeps the record the call left open: a result with a <c>continuation</c>
    /// replaces the stored one and gains <c>continuationExpiresAt</c>; any other result clears it, because
    /// every call on the screen session ends the record an earlier call left open.
    /// </summary>
    private static void AfterSuccess(CliServices services, string profile, string? fingerprint, UnwrappedToolResult result)
    {
        DateTimeOffset now = services.Clock.GetUtcNow();
        services.Profiles.UpdateState(profile, state =>
        {
            if (fingerprint is not null)
            {
                ScreenDuplicateGuard.Record(state, fingerprint, now);
            }

            state.LastContinuation = null;
            if (!result.IsRunning && result.Document["result"] is JsonObject body)
            {
                if (body["continuation"] is JsonObject continuation)
                {
                    DateTimeOffset expiresAt = ScreenContinuations.Remember(state, continuation, services.Mcp.SessionId, now);
                    body["continuationExpiresAt"] = ScreenContinuations.FormatTime(expiresAt);
                }
                else
                {
                    body["continuation"] = null;
                }
            }
        });
    }
}
