using Velixo.Cli.Commands;

namespace Velixo.Cli.Tests;

public sealed class VersionCommandTests
{
    [Theory]
    [InlineData("/opt/homebrew/Cellar/velixo/1.0.2/bin/velixo", false, "brew upgrade velixo")]
    [InlineData("/usr/local/Cellar/velixo/1.0.2/bin/velixo", false, "brew upgrade velixo")]
    [InlineData("/home/linuxbrew/.linuxbrew/Cellar/velixo/1.0.2/bin/velixo", false, "brew upgrade velixo")]
    [InlineData(@"C:\Users\ana\AppData\Local\Microsoft\WinGet\Packages\Velixo.CLI_Microsoft.Winget.Source_8wekyb3d8bbwe\velixo.exe", true, "winget upgrade Velixo.CLI")]
    [InlineData(@"C:\Users\ana\AppData\Local\Programs\velixo\velixo.exe", true, "irm https://cli.velixo.com/install.ps1 | iex")]
    [InlineData("/home/ana/.local/bin/velixo", false, "curl -fsSL https://cli.velixo.com/install.sh | sh")]
    [InlineData(null, false, "curl -fsSL https://cli.velixo.com/install.sh | sh")]
    public void UpdateCommand_follows_the_channel_the_binary_came_from(string? path, bool isWindows, string expected)
    {
        Assert.Equal(expected, VersionCommand.UpdateCommand(path, isWindows));
    }
}
