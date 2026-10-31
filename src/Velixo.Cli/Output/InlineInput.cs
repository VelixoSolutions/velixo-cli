using Velixo.Cli.Config;

namespace Velixo.Cli.Output;

/// <summary>The shell a command shown to the user is written for, so text it pipes in pastes as it is.</summary>
public enum InlineShell
{
    /// <summary>bash and zsh, and Git Bash on Windows: a quoted heredoc.</summary>
    Posix,

    /// <summary>PowerShell, the shell of a Windows terminal: a single-quoted here-string piped in.</summary>
    PowerShell,
}

/// <summary>Writes a command that reads <c>-</c> from stdin together with the text to feed it, in the form the user's shell takes.</summary>
public static class InlineInput
{
    /// <summary>
    /// PowerShell on Windows, unless the shell is Git Bash or another MSYS2 shell, which sets <c>MSYSTEM</c>;
    /// bash and zsh everywhere else.
    /// </summary>
    public static InlineShell Detect(IEnvironmentReader environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return environment.IsWindows && string.IsNullOrEmpty(environment.GetVariable("MSYSTEM"))
            ? InlineShell.PowerShell
            : InlineShell.Posix;
    }

    /// <summary>
    /// Quotes <paramref name="text"/> as one argument the shell passes on unchanged: in single quotes, with
    /// a single quote inside written as the shell spells it. Nothing in it is expanded, not even <c>$</c>.
    /// </summary>
    public static string Quote(InlineShell shell, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return shell == InlineShell.PowerShell
            ? "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'"
            : "'" + text.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// The command that sends <paramref name="path"/> to <paramref name="url"/> with a PUT, as an upload link
    /// takes it. PowerShell names <c>curl.exe</c>, since there <c>curl</c> is another command.
    /// </summary>
    public static string CurlUpload(InlineShell shell, string path, string url) =>
        $"{(shell == InlineShell.PowerShell ? "curl.exe" : "curl")} -T {Quote(shell, path)} {Quote(shell, url)}";

    /// <summary>
    /// Returns the lines that run <paramref name="command"/> with <paramref name="text"/> on its stdin, each
    /// at the margin, or null when the text holds the line that would end it early. Both forms are
    /// quoted, so the shell changes nothing in the text: no <c>$</c> or backtick expansion.
    /// </summary>
    /// <param name="label">The heredoc's end word, such as <c>VQL</c>; PowerShell does not use one.</param>
    public static IReadOnlyList<string>? Lines(InlineShell shell, string command, string text, string label)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(text);

        var body = text.ReplaceLineEndings("\n").TrimEnd().Split('\n');
        var end = shell == InlineShell.PowerShell ? "'@" : label;
        if (body.Any(line => shell == InlineShell.PowerShell ? line.StartsWith(end, StringComparison.Ordinal) : line == end))
        {
            return null;
        }

        return shell == InlineShell.PowerShell
            ? ["@'", .. body, $"'@ | {command}"]
            : [$"{command} <<'{label}'", .. body, label];
    }
}
