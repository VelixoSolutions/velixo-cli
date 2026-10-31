using System.CommandLine;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Velixo.Cli.Commands;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed partial class GuideCommandTests
{
    /// <summary>The page that may name tools, because it is about the raw tools.</summary>
    private const string RawToolsPage = "setup";

    private static readonly string[] _toolNames =
    [
        McpToolNames.ListConnections, McpToolNames.ToolGuidance, McpToolNames.PrepareQuery, McpToolNames.ExecuteQuery,
        McpToolNames.CreateWorkbookFromQuery, McpToolNames.PrepareCommands, McpToolNames.ExecuteCommands, McpToolNames.GetStatus,
        McpToolNames.GetUploadUrl, McpToolNames.ListStagedFiles, McpToolNames.ListImportScenarios, McpToolNames.ExecuteImportScenario,
        McpToolNames.SendNotification, McpToolNames.SignOut,
    ];

    [Fact]
    public void Every_command_has_exactly_one_page_and_every_page_names_real_commands()
    {
        using var test = new TestServices();
        var leaves = Leaves(Program.BuildRootCommand(test.Services), string.Empty).ToList();
        var covered = GuidePages.All.SelectMany(page => page.Commands).ToList();

        var notOnce = leaves.Where(leaf => covered.Count(command => command == leaf) != 1).ToList();

        Assert.Contains(GuidePages.All, page => page.Name == GuidePages.GetStarted);
        Assert.Empty(notOnce);
        Assert.Empty(covered.Except(leaves));
    }

    [Fact]
    public void Every_command_and_flag_a_page_mentions_exists()
    {
        using var test = new TestServices();
        RootCommand root = Program.BuildRootCommand(test.Services);
        var flags = AllOptions(root).SelectMany(option => option.Aliases.Append(option.Name)).ToHashSet(StringComparer.Ordinal);
        flags.UnionWith(["--help", "--version"]);

        var problems = new List<string>();
        foreach (GuidePage page in GuidePages.All)
        {
            foreach (Match mention in CommandMention().Matches(page.Markdown))
            {
                if (!IsCommandLine(root, mention.Groups["words"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                {
                    problems.Add($"{page.Name}: velixo{mention.Groups["words"].Value}");
                }
            }

            problems.AddRange(FlagMention().Matches(page.Markdown)
                .Select(flag => flag.Value)
                .Where(flag => !flags.Contains(flag))
                .Select(flag => $"{page.Name}: {flag}"));
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Only_the_raw_tools_page_names_the_servers_tools()
    {
        var mentions = GuidePages.All
            .Where(page => page.Name != RawToolsPage)
            .SelectMany(page => _toolNames.Where(tool => page.Markdown.Contains(tool, StringComparison.Ordinal)).Select(tool => $"{page.Name}: {tool}"));

        Assert.Empty(mentions);
    }

    [Fact]
    public void Words_resolve_to_a_command_page_a_group_or_a_page_name()
    {
        Assert.Equal(["query-run"], GuidePages.Resolve(["query", "run"]).Select(page => page.Name));
        Assert.Equal(["query-run"], GuidePages.Resolve(["query run"]).Select(page => page.Name));
        Assert.Equal(["command-build", "command-run", "library"], GuidePages.Resolve(["command"]).Select(page => page.Name));
        Assert.Equal(["files", "notify"], GuidePages.Resolve(["files", "notify"]).Select(page => page.Name));
        Assert.Equal(["sign-in"], GuidePages.Resolve(["auth", "token"]).Select(page => page.Name));
    }

    [Fact]
    public async Task A_bare_guide_prints_get_started_without_the_network()
    {
        using var test = new TestServices();
        var mcp = new FakeMcpClient();
        test.Services.McpClientFactory = _ => mcp;

        var exit = await test.RunAsync("guide");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.StartsWith("# Velixo CLI: get started", test.Console.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("<!--", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Empty(mcp.Calls);
    }

    [Fact]
    public async Task The_guide_in_json_names_each_page_and_its_commands()
    {
        using var test = new TestServices();

        var exit = await test.RunAsync("guide", "import", "--output", "json");

        Assert.Equal(ExitCodes.Success, exit);
        JsonNode section = JsonNode.Parse(test.Console.StdOut)!["sections"]![0]!;
        Assert.Equal("import", (string?)section["name"]);
        Assert.Equal(["import scenarios", "import run"], section["commands"]!.AsArray().Select(name => (string?)name));
    }

    [Fact]
    public async Task An_unknown_topic_lists_the_pages()
    {
        using var test = new TestServices();

        var exit = await test.RunAsync("guide", "nonsense");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("get-started", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_names_map_to_the_tools_they_call()
    {
        IReadOnlyList<string> names = CommandMap.ToToolNames(["get_started", "command", "run", "execute_query", "notify"]);

        Assert.Equal(["get_started", McpToolNames.ExecuteCommands, McpToolNames.ExecuteQuery, McpToolNames.SendNotification], names);
    }

    [Fact]
    public void The_servers_guidance_splits_into_the_recipe_and_one_section_per_tool()
    {
        const string markdown = "# Get started\nRecipe.\n\n## prepare_query\nBuild rules.\n\n## execute_query\nRun rules.";

        var sections = ToolsCommands.SplitGuidance(markdown, ["get_started", "prepare_query", "execute_query"]);

        Assert.Equal(3, sections.Count);
        Assert.Equal("get_started", (string?)sections[0]!["name"]);
        Assert.Equal("## execute_query\nRun rules.", (string?)sections[2]!["markdown"]);
    }

    /// <summary>Whether the words after <c>velixo</c> start with a command: a leaf ends the walk, and a group needs one of its commands or nothing.</summary>
    private static bool IsCommandLine(Command root, IReadOnlyList<string> words)
    {
        Command current = root;
        foreach (var word in words)
        {
            if (current.Subcommands.FirstOrDefault(child => child.Name == word) is { } next)
            {
                current = next;
                continue;
            }

            return current != root && current.Subcommands.Count == 0;
        }

        return current != root;
    }

    private static IEnumerable<Option> AllOptions(Command command) =>
        command.Options.Concat(command.Subcommands.SelectMany(AllOptions));

    private static IEnumerable<string> Leaves(Command command, string path) =>
        command.Subcommands.Count == 0
            ? [path]
            : command.Subcommands.Where(child => !child.Hidden).SelectMany(child => Leaves(child, (path + " " + child.Name).Trim()));

    [GeneratedRegex(@"(?<![\w./-])velixo(?<words>(?: [a-z][a-z-]*)+)")]
    private static partial Regex CommandMention();

    [GeneratedRegex(@"(?<![\w-])--[a-z][a-z0-9-]*")]
    private static partial Regex FlagMention();
}
