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
}
