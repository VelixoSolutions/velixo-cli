using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class InlineInputTests
{
    [Theory]
    [InlineData(false, null, InlineShell.Posix)]
    [InlineData(true, null, InlineShell.PowerShell)]
    [InlineData(true, "MINGW64", InlineShell.Posix)]
    public void The_shell_follows_the_os_and_git_bash_counts_as_bash(bool windows, string? msystem, InlineShell expected)
    {
        var environment = new TestEnvironment(".") { IsWindows = windows };
        if (msystem is not null)
        {
            environment.Variables["MSYSTEM"] = msystem;
        }

        Assert.Equal(expected, InlineInput.Detect(environment));
    }

    [Fact]
    public void Bash_gets_a_quoted_heredoc_at_the_margin()
    {
        var lines = InlineInput.Lines(InlineShell.Posix, "velixo query run --query -", "FROM A\r\nSELECT '$X'\n", "VQL");

        Assert.Equal(["velixo query run --query - <<'VQL'", "FROM A", "SELECT '$X'", "VQL"], lines);
    }

    [Fact]
    public void PowerShell_gets_a_single_quoted_here_string_piped_in()
    {
        var lines = InlineInput.Lines(InlineShell.PowerShell, "velixo query run --query -", "FROM A\nSELECT '$X'", "VQL");

        Assert.Equal(["@'", "FROM A", "SELECT '$X'", "'@ | velixo query run --query -"], lines);
    }

    [Theory]
    [InlineData(InlineShell.Posix, "FROM A\nVQL\nSELECT X")]
    [InlineData(InlineShell.PowerShell, "FROM A\n'@ SELECT X")]
    public void Text_holding_the_end_line_gets_no_inline_form(InlineShell shell, string text)
    {
        Assert.Null(InlineInput.Lines(shell, "velixo query run --query -", text, "VQL"));
    }
}
