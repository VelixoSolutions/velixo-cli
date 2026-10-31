using System.CommandLine;
using Velixo.Cli.Commands;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class WelcomeScreenTests
{
    [Fact]
    public async Task A_bare_velixo_in_a_terminal_shows_the_welcome_screen()
    {
        using var test = new TestServices(redirected: false);

        var exit = await test.RunAsync();

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("██", test.Console.StdOut);
        Assert.Contains("Velixo CLI " + CliInfo.Version, test.Console.StdOut);
        Assert.Contains("velixo login", test.Console.StdOut);
        Assert.Contains("\u001b[38;2;", test.Console.StdOut);
    }

    [Fact]
    public void The_welcome_screen_lists_every_command_of_the_tree_and_nothing_else()
    {
        using var test = new TestServices();
        var tree = Leaves(Program.BuildRootCommand(test.Services), string.Empty).ToHashSet(StringComparer.Ordinal);
        var listed = WelcomeScreen.Groups.SelectMany(group => group.Commands).Select(entry => entry.Command).ToList();

        Assert.Equal(listed.Count, listed.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(tree.Except(listed));
        Assert.Empty(listed.Except(tree));
    }

    [Fact]
    public async Task The_welcome_screen_shows_every_command()
    {
        using var test = new TestServices(redirected: false);

        await test.RunAsync();

        Assert.All(
            WelcomeScreen.Groups.SelectMany(group => group.Commands),
            entry => Assert.Contains("velixo " + entry.Command + " ", test.Console.StdOut, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_bare_velixo_in_a_pipe_prints_the_help_text()
    {
        using var test = new TestServices();

        var exit = await test.RunAsync();

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("██", test.Console.StdOut);
        Assert.Contains("login", test.Console.StdOut);
        Assert.Contains("query", test.Console.StdOut);
    }

    [Fact]
    public async Task An_agent_at_a_terminal_gets_the_help_text_not_the_logo()
    {
        using var test = new TestServices(new Dictionary<string, string> { ["CLAUDECODE"] = "1" }, redirected: false);

        var exit = await test.RunAsync();

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("██", test.Console.StdOut);
        Assert.Contains("query", test.Console.StdOut);
    }

    [Fact]
    public async Task No_color_strips_the_escapes_but_keeps_the_mark()
    {
        using var test = new TestServices(new Dictionary<string, string> { ["NO_COLOR"] = "1" }, redirected: false);

        var exit = await test.RunAsync();

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("██", test.Console.StdOut);
        Assert.DoesNotContain("\u001b[", test.Console.StdOut);
    }

    [Fact]
    public void A_narrow_terminal_gets_the_plain_name()
    {
        Assert.Equal("Velixo", VelixoLogo.Render(useColor: false, width: 40));
        Assert.Contains("██", VelixoLogo.Render(useColor: false, width: 0));
    }

    [Fact]
    public void A_wide_terminal_gets_the_symbol_beside_the_name_and_a_medium_one_the_name_alone()
    {
        var full = VelixoLogo.Render(useColor: false, width: 120);
        var nameOnly = VelixoLogo.Render(useColor: false, width: 60);

        Assert.Equal(VelixoSymbol.Rows, full.Split('\n').Length);
        Assert.Contains("▀", full);
        Assert.Equal(6, nameOnly.Split('\n').Length);
        Assert.DoesNotContain("▀", nameOnly);
    }

    [Fact]
    public void The_symbol_cells_are_well_formed()
    {
        Assert.Equal(VelixoSymbol.Rows, VelixoSymbol.Cells.Length);
        Assert.All(VelixoSymbol.Cells, row => Assert.Equal(VelixoSymbol.Columns * 12, row.Length));
    }

    private static IEnumerable<string> Leaves(Command command, string path) =>
        command.Subcommands.Count == 0
            ? [path]
            : command.Subcommands.Where(child => !child.Hidden).SelectMany(child => Leaves(child, (path + " " + child.Name).Trim()));
}
