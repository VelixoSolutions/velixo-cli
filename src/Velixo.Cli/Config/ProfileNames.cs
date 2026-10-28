using System.Text;
using Velixo.Cli.Output;

namespace Velixo.Cli.Config;

/// <summary>
/// The rule for a profile name. A name becomes a file name under <c>state/</c>, so it is limited to
/// letters, digits, dot, dash and underscore.
/// </summary>
public static class ProfileNames
{
    private const int MaxLength = 64;

    public static bool IsValid(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= MaxLength
        && char.IsAsciiLetterOrDigit(name[0])
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    public static string Validate(string name) =>
        IsValid(name)
            ? name
            : throw CliException.Usage(
                $"'{name}' is not a valid profile name.",
                "Use letters, digits, '.', '-' and '_', starting with a letter or digit.");

    /// <summary>A profile name for an MCP URL that is not a built-in environment: its host, made safe.</summary>
    public static string FromUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var name = new StringBuilder();
        foreach (var c in url.IsDefaultPort ? url.Host : $"{url.Host}-{url.Port}")
        {
            name.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-');
        }

        var text = name.ToString().TrimStart('.', '-', '_');
        return IsValid(text) ? text : "custom";
    }
}
