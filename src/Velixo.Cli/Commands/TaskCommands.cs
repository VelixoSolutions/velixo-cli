using System.CommandLine;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary><c>velixo task status</c>: <c>get_status</c>. Declared with the options of the spec, section 8.5; not implemented in this build.</summary>
public static class TaskCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var taskId = new Argument<string>("task-id") { Description = "The taskId a command printed with --no-wait or on a timeout." };
        var wait = new Option<bool>("--wait") { Description = "Keep waiting until the task is done, failed or expired." };

        var status = new Command("status", "Follow a task. Example: velixo task status t_8f3a --wait") { taskId, wait };
        status.SetAction(_ => NotImplemented("velixo task status"));

        return new Command("task", "Follow tasks that commands started.") { status };
    }

    private static int NotImplemented(string path) => throw CliException.NotImplemented(path);
}
