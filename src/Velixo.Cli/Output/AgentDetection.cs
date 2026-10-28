namespace Velixo.Cli.Output;

/// <summary>
/// Names the AI agent the CLI runs under, from the environment variables the agents set. A detected
/// agent turns non-interactive mode on and is sent to the server as <c>_meta.agent</c>.
/// </summary>
public static class AgentDetection
{
    /// <summary>Each variable that marks an agent, with the name the CLI reports for it, in the order they are checked.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> Markers =
    [
        new("CLAUDECODE", "claude-code"),
        new("CURSOR_AGENT", "cursor"),
        new("CODEX_SANDBOX", "codex"),
        new("CODEX_THREAD_ID", "codex"),
        new("GEMINI_CLI", "gemini-cli"),
        new("CLINE_ACTIVE", "cline"),
        new("OPENCODE", "opencode"),
        new("KIRO_SESSION_ID", "kiro"),
    ];

    /// <summary>The generic variable any agent may set; its value is the agent's name.</summary>
    public const string GenericVariable = "AI_AGENT";

    public static string? Detect(Func<string, string?> getVariable)
    {
        ArgumentNullException.ThrowIfNull(getVariable);

        foreach ((var variable, var agent) in Markers)
        {
            if (!string.IsNullOrEmpty(getVariable(variable)))
            {
                return agent;
            }
        }

        var generic = getVariable(GenericVariable);
        return string.IsNullOrWhiteSpace(generic) ? null : generic.Trim();
    }
}
