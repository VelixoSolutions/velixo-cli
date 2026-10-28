using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Mcp;

/// <summary>
/// Waits for a task the way the server intends: <c>get_status</c> long-polls, so each call holds
/// until the task finishes or a short hold elapses, and the follower calls it again at once. Progress
/// goes to stderr; the finished result is returned unwrapped.
/// </summary>
public sealed class TaskFollower(IMcpClient mcp, OutputWriter output, TimeProvider clock)
{
    /// <summary>A task that has made no progress for this long gets "no change for …" on its progress line.</summary>
    private static readonly TimeSpan _stallNotice = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Starts a task with <paramref name="start"/> and follows it to its end, all within
    /// <see cref="TaskFollowOptions.Timeout"/>. With <c>--no-wait</c> it returns the first answer, which is the
    /// <c>{ taskId, status: "running" }</c> envelope when the task did not finish in the first hold.
    /// </summary>
    public async Task<UnwrappedToolResult> RunAsync(
        Func<CancellationToken, Task<McpToolResult>> start,
        TaskFollowOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(options);

        using CancellationTokenSource deadline = CreateDeadline(options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        UnwrappedToolResult first;
        try
        {
            first = ToolResultUnwrapper.Unwrap(await start(linked.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw TimedOut(taskId: null);
        }

        return await FollowCoreAsync(first, options, deadline, linked.Token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Follows a task whose envelope the caller already holds, such as <c>velixo task status</c>.</summary>
    public async Task<UnwrappedToolResult> FollowAsync(
        UnwrappedToolResult started,
        TaskFollowOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(started);
        ArgumentNullException.ThrowIfNull(options);

        using CancellationTokenSource deadline = CreateDeadline(options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        return await FollowCoreAsync(started, options, deadline, linked.Token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The progress line for one <c>get_status</c> answer, such as <c>Release on SO301000, 2m14s, no change for 48s</c>.</summary>
    public static string DescribeProgress(string? taskId, JsonObject? progress)
    {
        var label = (string?)progress?["label"];
        var parts = new List<string> { string.IsNullOrWhiteSpace(label) ? $"Waiting for task {taskId}" : label };

        if (progress?["elapsedMs"] is JsonValue elapsed && elapsed.TryGetValue(out long elapsedMs))
        {
            parts.Add(DurationText.FromMilliseconds(elapsedMs));
        }

        if (progress?["stalledForMs"] is JsonValue stalled && stalled.TryGetValue(out long stalledMs)
            && TimeSpan.FromMilliseconds(stalledMs) >= _stallNotice)
        {
            parts.Add($"no change for {DurationText.FromMilliseconds(stalledMs)}");
        }

        return string.Join(", ", parts);
    }

    private static CliException TimedOut(string? taskId) =>
        new(
            ExitCodes.Timeout,
            ErrorCodes.Timeout,
            "The task was still running when --timeout ran out; it keeps running on the server.",
            taskId is null ? "Run the command again with a longer --timeout." : $"velixo task status {taskId} --wait")
        {
            TaskId = taskId,
        };

    private CancellationTokenSource CreateDeadline(TimeSpan? timeout) =>
        timeout is { } limit ? new CancellationTokenSource(limit, clock) : new CancellationTokenSource();

    private async Task<UnwrappedToolResult> FollowCoreAsync(
        UnwrappedToolResult current,
        TaskFollowOptions options,
        CancellationTokenSource deadline,
        CancellationToken linkedToken,
        CancellationToken callerToken)
    {
        if (!current.IsRunning || options.NoWait)
        {
            return current;
        }

        try
        {
            while (current.IsRunning)
            {
                output.Progress(DescribeProgress(current.TaskId, current.Progress));

                McpToolResult polled = await mcp
                    .CallToolAsync(McpToolNames.GetStatus, new JsonObject { ["taskId"] = current.TaskId }, linkedToken)
                    .ConfigureAwait(false);
                current = ToolResultUnwrapper.Unwrap(polled);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !callerToken.IsCancellationRequested)
        {
            throw TimedOut(current.TaskId);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw new CliException(
                ExitCodes.Interrupted,
                ErrorCodes.Interrupted,
                "Interrupted; the task keeps running on the server.",
                $"velixo task status {current.TaskId} --wait")
            {
                TaskId = current.TaskId,
            };
        }

        return current;
    }
}
