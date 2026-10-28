using Velixo.Cli.Commands;
using Velixo.Cli.Mcp;

namespace Velixo.Cli.Tests;

public sealed class GuideCommandTests
{
    [Fact]
    public void Command_names_map_to_the_tools_they_call()
    {
        IReadOnlyList<string> names = CommandMap.ToToolNames(["get_started", "screen", "run", "execute_query", "notify"]);

        Assert.Equal(["get_started", McpToolNames.ExecuteCommands, McpToolNames.ExecuteQuery, McpToolNames.SendNotification], names);
    }

    [Fact]
    public void Guidance_splits_into_the_recipe_and_one_section_per_tool()
    {
        const string markdown = "# Get started\nRecipe.\n\n## prepare_query\nBuild rules.\n\n## execute_query\nRun rules.";

        var sections = GuideCommand.Split(markdown, ["get_started", "prepare_query", "execute_query"]);

        Assert.Equal(3, sections.Count);
        Assert.Equal("get_started", (string?)sections[0]!["name"]);
        Assert.Equal("## execute_query\nRun rules.", (string?)sections[2]!["markdown"]);
    }

    [Fact]
    public void The_agent_guide_is_embedded()
    {
        Assert.Contains("Exit codes", GuideCommand.ReadAgentGuide(), StringComparison.Ordinal);
    }
}
