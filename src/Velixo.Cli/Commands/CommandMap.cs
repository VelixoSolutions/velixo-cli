using Velixo.Cli.Mcp;

namespace Velixo.Cli.Commands;

/// <summary>Which tool each command calls, so <c>velixo tools guidance command run</c> fetches the guidance of <c>execute_commands</c>.</summary>
public static class CommandMap
{
    public const string GetStarted = "get_started";

    public static readonly IReadOnlyDictionary<string, string> ToolByCommand = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["connections list"] = McpToolNames.ListConnections,
        ["connections"] = McpToolNames.ListConnections,
        ["query build"] = McpToolNames.PrepareQuery,
        ["query run"] = McpToolNames.ExecuteQuery,
        ["query workbook"] = McpToolNames.CreateWorkbookFromQuery,
        ["command build"] = McpToolNames.PrepareCommands,
        ["command run"] = McpToolNames.ExecuteCommands,
        ["task status"] = McpToolNames.GetStatus,
        ["import scenarios"] = McpToolNames.ListImportScenarios,
        ["import run"] = McpToolNames.ExecuteImportScenario,
        ["notify"] = McpToolNames.SendNotification,
        ["logout"] = McpToolNames.SignOut,
    };

    /// <summary>
    /// The guidance section names for the words after <c>velixo tools guidance</c>: a tool name as itself, a
    /// command of one or two words as the tool it calls, anything else as given, since the server drops
    /// a name it does not know.
    /// </summary>
    public static IReadOnlyList<string> ToToolNames(IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        var names = new List<string>();
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index].Trim();
            if (index + 1 < words.Count && ToolByCommand.TryGetValue($"{word} {words[index + 1].Trim()}", out var pairTool))
            {
                names.Add(pairTool);
                index++;
            }
            else if (ToolByCommand.TryGetValue(word, out var singleTool))
            {
                names.Add(singleTool);
            }
            else if (word.Length > 0)
            {
                names.Add(word);
            }
        }

        return names.Distinct(StringComparer.Ordinal).ToList();
    }
}
