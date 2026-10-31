using System.Text.RegularExpressions;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>One page of the guide: its name, the commands it covers, and its Markdown.</summary>
public sealed record GuidePage(string Name, IReadOnlyList<string> Commands, string Markdown);

/// <summary>
/// The pages of <c>velixo guide</c>, embedded from <c>docs/guide/*.md</c>. Each page opens with
/// <c>&lt;!-- commands: query build, query run --&gt;</c>, which names the commands it covers, so
/// <c>velixo guide query run</c> finds its page and a test can hold the pages to the command tree.
/// </summary>
public static partial class GuidePages
{
    public const string GetStarted = "get-started";

    private const string ResourcePrefix = "Velixo.Cli.Guide.";

    private static readonly Lazy<IReadOnlyList<GuidePage>> _all = new(Load);

    public static IReadOnlyList<GuidePage> All => _all.Value;

    /// <summary>
    /// Returns the pages for the words after <c>velixo guide</c>, read left to right: a command of two
    /// words or one (<c>query run</c>, <c>notify</c>), a page name (<c>files</c>), or a command group
    /// (<c>screen</c>), which brings every page of the group.
    /// </summary>
    /// <exception cref="CliException">Exit 2 for a word that names no page, with the topics there are.</exception>
    public static IReadOnlyList<GuidePage> Resolve(IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        var found = new List<GuidePage>();
        var tokens = words.SelectMany(word => word.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(word => word.ToLowerInvariant())
            .ToList();

        for (var index = 0; index < tokens.Count; index++)
        {
            if (index + 1 < tokens.Count && ForCommand($"{tokens[index]} {tokens[index + 1]}") is { } pair)
            {
                found.Add(pair);
                index++;
                continue;
            }

            var word = tokens[index];
            if ((ForCommand(word) ?? All.FirstOrDefault(page => page.Name == word)) is { } single)
            {
                found.Add(single);
                continue;
            }

            var group = All.Where(page => page.Commands.Any(command => command.StartsWith(word + " ", StringComparison.Ordinal))).ToList();
            if (group.Count == 0)
            {
                throw CliException.Usage(
                    $"The guide has no page for '{word}'.",
                    $"Name a command, such as 'velixo guide query run', or one of these pages: {string.Join(", ", All.Select(page => page.Name))}.");
            }

            found.AddRange(group);
        }

        return found.Distinct().ToList();
    }

    public static GuidePage? ForCommand(string command) =>
        All.FirstOrDefault(page => page.Commands.Contains(command, StringComparer.Ordinal));

    /// <summary>Reads a page's text: the <c>commands</c> line names what it covers, and the rest is the page.</summary>
    public static GuidePage Parse(string name, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Match header = CommandsLine().Match(text);
        IReadOnlyList<string> commands = header.Success
            ? header.Groups["commands"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        var markdown = header.Success ? text[header.Length..] : text;
        return new GuidePage(name, commands, markdown.Trim());
    }

    private static List<GuidePage> Load()
    {
        var assembly = typeof(GuidePages).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(resource => resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) && resource.EndsWith(".md", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(resource =>
            {
                using Stream stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                return Parse(resource[ResourcePrefix.Length..^".md".Length], reader.ReadToEnd());
            })
            .ToList();
    }

    [GeneratedRegex(@"\A\s*<!--\s*commands:(?<commands>[^>]*)-->\s*")]
    private static partial Regex CommandsLine();
}
