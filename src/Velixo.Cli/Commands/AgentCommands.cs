using System.CommandLine;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary><c>velixo agent setup</c>: installs the velixo-cli skill for an AI agent. Declared with the options of the spec, section 8.10; not implemented in this build.</summary>
public static class AgentCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var client = new Option<string?>("--client") { Description = "The agent: claude-code, codex or cursor. Detected by default.", HelpName = "client" };
        client.AcceptOnlyFromAmong("claude-code", "codex", "cursor");
        var scope = new Option<string?>("--scope") { Description = "local (this project) or global.", HelpName = "scope" };
        scope.AcceptOnlyFromAmong("local", "global");
        var json = new Option<bool>("--json") { Description = "Print the planned actions without doing them." };

        var setup = new Command("setup", "Install the velixo-cli skill for an AI agent.") { client, scope, json };
        setup.SetAction(_ => NotImplemented("velixo agent setup"));

        return new Command("agent", "Set up AI agents to use the CLI.") { setup };
    }

    private static int NotImplemented(string path) => throw CliException.NotImplemented(path);
}
