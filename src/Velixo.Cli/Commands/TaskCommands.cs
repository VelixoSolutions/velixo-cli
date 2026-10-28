using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Query;

namespace Velixo.Cli.Commands;

/// <summary><c>velixo task status</c>: <c>get_status</c>, as the spec's section 8.5 describes it.</summary>
public static class TaskCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var taskId = new Argument<string>("task-id") { Description = "The taskId a command printed with --no-wait or on a timeout." };
        var wait = new Option<bool>("--wait") { Description = "Keep waiting until the task is done, failed or expired, with progress on stderr." };

        var status = new Command(
            "status",
            "Read a task's state with one long poll, or follow it to its end with --wait. Example: velixo task status t_8f3a --wait")
        {
            taskId,
            wait,
        };

        status.SetAction(async (parse, cancellationToken) =>
        {
            var arguments = new JsonObject { ["taskId"] = parse.GetValue(taskId) };
            TaskFollowOptions options = services.FollowOptions() with { NoWait = !parse.GetValue(wait) };

            UnwrappedToolResult result = await services.Tasks.RunAsync(
                token => services.Mcp.CallToolAsync(McpToolNames.GetStatus, arguments, token),
                options,
                cancellationToken).ConfigureAwait(false);

            var document = (JsonObject)result.Document.DeepClone();
            QueryPlan.NormalizeItemKeys(document);
            QueryOutput.RelayWarnings(services.Output, document);

            if (QueryOutput.IsQueryResult(document))
            {
                QueryOutput.WriteResult(services.Output, document, skip: 0);
            }
            else
            {
                QueryOutput.WritePlan(services.Output, document, savedTo: null);
            }

            return ExitCodes.Success;
        });

        return new Command("task", "Follow tasks that commands started.") { status };
    }
}
