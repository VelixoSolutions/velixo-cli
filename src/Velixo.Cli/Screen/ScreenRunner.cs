using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Config;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>
/// <c>velixo screen run</c>: checks the batch locally, confirms a write, refuses a duplicate, stages
/// the attachments, calls <c>execute_commands</c>, and keeps the record a <c>--leave-open</c> call
/// left open for the next <c>--continue</c>.
/// </summary>
internal static class ScreenRunner
{
    public static async Task<int> RunAsync(CliServices services, ScreenRunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var intent = services.RequireIntent();
        if (options.Continue && options.ContinuationFile is not null)
        {
            throw CliException.Usage("Give --continue or --continuation FILE, not both.");
        }

        var directory = services.Environment.CurrentDirectory;
        string FullPath(string path) => Path.GetFullPath(path, directory);

        ScreenBatch batch = ScreenBatch.Read(FullPath(options.BatchPath));
        JsonObject variables = ScreenVariables.Bind(
            options.VariablesFile is null ? null : FullPath(options.VariablesFile),
            options.Variables,
            options.NullVariables);
        JsonArray? rows = options.RowsFile is null ? null : ScreenRows.Read(FullPath(options.RowsFile));
        var attachments = options.Attachments.Select(value => ScreenAttachmentSpec.Parse(value, directory)).ToList();

        var profile = services.Settings.ProfileName;
        ProfileState state = services.Profiles.LoadState(profile);
        ScreenContinuationSource? continuation = options switch
        {
            { Continue: true } => ScreenContinuations.LoadStored(state),
            { ContinuationFile: { } file } => ScreenContinuations.ReadFile(FullPath(file), state),
            _ => null,
        };

        var plan = ScreenRunPlan.Create(batch, variables, rows, continuation, options.LeaveOpen, options.Finish, attachments);
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
            Confirm(services, plan.Summary(connection));
        }

        JsonArray? staged = attachments.Count > 0
            ? await ScreenAttachmentStager.StageAsync(services, attachments, cancellationToken).ConfigureAwait(false)
            : null;
        JsonObject arguments = plan.ToArguments(intent, connection, staged);

        UnwrappedToolResult result;
        try
        {
            result = await services.Tasks
                .RunAsync(token => services.Mcp.CallToolAsync(McpToolNames.ExecuteCommands, arguments, token), TaskFollowOptions.From(services.Settings), cancellationToken)
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
            await ScreenAttachmentDownloader.DownloadAsync(services, files, downloads, cancellationToken).ConfigureAwait(false);
        }

        CommandText.RelayWarnings(services.Output, result.Document);
        services.Output.WriteResult(result.Document);
        return ExitCodes.Success;
    }

    /// <summary>
    /// Returns the MCP session the next call goes out in: the client's, or before its first request
    /// the one stored for this server, which is the one the client will send.
    /// </summary>
    private static string? CurrentSessionId(CliServices services, ProfileState state) =>
        services.Mcp.SessionId
        ?? (string.Equals(state.McpUrl, services.Settings.McpUrl.ToString(), StringComparison.Ordinal) ? state.McpSessionId : null);

    /// <summary>
    /// Asks for the go-ahead of a write: <c>--yes</c> gives it, a terminal asks <c>Run this batch? [y/N]</c>,
    /// and without either the command stops with exit 2. The summary goes to stderr in every case.
    /// </summary>
    private static void Confirm(CliServices services, IReadOnlyList<string> summary)
    {
        if (services.Settings.AssumeYes || !services.Settings.IsInteractive)
        {
            foreach (var line in summary)
            {
                services.Output.Progress(line);
            }

            if (services.Settings.AssumeYes)
            {
                return;
            }

            throw new CliException(
                ExitCodes.Usage,
                ErrorCodes.ConfirmationRequired,
                "This batch can write to the ERP, and nothing confirmed it: the CLI cannot ask without a terminal. The summary is on stderr.",
                "Tell the user in business terms what the batch will do and ask them; once they agree, run the same command again with --yes.");
        }

        foreach (var line in summary)
        {
            services.Console.Error.WriteLine(line);
        }

        services.Console.Error.Write("Run this batch? [y/N] ");
        var answer = services.Console.In.ReadLine()?.Trim();
        if (string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new CliException(ExitCodes.Usage, ScreenErrorCodes.ConfirmationDeclined, "The batch did not run: the confirmation was declined.");
    }

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

        ProfileState state = services.Profiles.LoadState(profile);
        if (record)
        {
            ScreenDuplicateGuard.Record(state, fingerprint!, services.Clock.GetUtcNow());
        }

        if (endsContinuation)
        {
            state.LastContinuation = null;
        }

        services.Profiles.SaveState(profile, state);
    }

    /// <summary>
    /// Records the write and keeps the record the call left open: a result with a <c>continuation</c>
    /// replaces the stored one and gains <c>continuationExpiresAt</c>; any other result clears it, because
    /// every call on the screen session ends the record an earlier call left open.
    /// </summary>
    private static void AfterSuccess(CliServices services, string profile, string? fingerprint, UnwrappedToolResult result)
    {
        DateTimeOffset now = services.Clock.GetUtcNow();
        ProfileState state = services.Profiles.LoadState(profile);
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

        services.Profiles.SaveState(profile, state);
    }
}
