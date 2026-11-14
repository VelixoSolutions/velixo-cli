using System.CommandLine;
using System.CommandLine.Help;
using System.Reflection;

namespace Velixo.Cli.IntegrationTests;

/// <summary>
/// Offline checks that the live tests cover the whole command tree of <see cref="Program.BuildRootCommand"/>:
/// every command in a <see cref="CoversAttribute"/>, every flag in a <see cref="CoversFlagsAttribute"/>
/// or in <see cref="Untestable"/> with the reason it cannot run live.
/// </summary>
public sealed class CoverageTests
{
    /// <summary>
    /// The flags no live test passes, keyed <c>command --flag</c> (<c>* --flag</c> for a shared one), each
    /// with the reason. Empty while every flag has a live test; a flag added here must say why.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Untestable = new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly Lazy<Command> _root = new(() => Program.BuildRootCommand(CliServices.CreateDefault()));

    [Fact]
    public void Every_command_has_a_live_test()
    {
        var covered = LiveTestMethods()
            .SelectMany(method => method.GetCustomAttribute<CoversAttribute>()?.Commands ?? [])
            .ToHashSet(StringComparer.Ordinal);
        var commands = Leaves(_root.Value, string.Empty).Select(leaf => leaf.Path).ToList();

        var untested = commands.Where(command => !covered.Contains(command)).ToList();
        var unknown = covered.Except(commands).ToList();

        Assert.True(untested.Count == 0, $"No live test covers these commands: {string.Join(", ", untested)}");
        Assert.True(unknown.Count == 0, $"Not commands of the command tree: {string.Join(", ", unknown)}");
    }

    [Fact]
    public void Every_flag_has_a_live_test_or_a_reason_it_has_none()
    {
        var flags = Flags().ToHashSet(StringComparer.Ordinal);
        var covered = LiveTestMethods()
            .SelectMany(method => method.GetCustomAttributes<CoversFlagsAttribute>())
            .SelectMany(attribute => attribute.Flags.Select(flag => $"{attribute.Command} {flag}"))
            .ToHashSet(StringComparer.Ordinal);

        var untested = flags.Where(flag => !covered.Contains(flag) && !Untestable.ContainsKey(flag)).Order(StringComparer.Ordinal).ToList();
        var unknown = covered.Concat(Untestable.Keys).Where(flag => !flags.Contains(flag)).Order(StringComparer.Ordinal).ToList();
        var exemptButTested = Untestable.Keys.Where(covered.Contains).ToList();
        var exemptWithoutReason = Untestable.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).ToList();

        Assert.True(untested.Count == 0, $"No live test passes these flags; add [CoversFlags] to a test that does, or a reason to Untestable: {string.Join(", ", untested)}");
        Assert.True(unknown.Count == 0, $"Not flags of the command tree: {string.Join(", ", unknown)}");
        Assert.True(exemptButTested.Count == 0, $"Listed as untestable, yet a live test covers them: {string.Join(", ", exemptButTested)}");
        Assert.True(exemptWithoutReason.Count == 0, $"Untestable without a reason: {string.Join(", ", exemptWithoutReason)}");
    }

    /// <summary>Every flag, as <c>command --flag</c>, and the shared ones as <c>* --flag</c>.</summary>
    private static IEnumerable<string> Flags()
    {
        foreach (Option option in CliServices.CreateDefault().Options.All)
        {
            yield return $"{CoversFlagsAttribute.Shared} {option.Name}";
        }

        foreach ((string path, Command command) in Leaves(_root.Value, string.Empty))
        {
            foreach (Option option in command.Options.Where(option => !option.Hidden && option is not HelpOption))
            {
                yield return $"{path} {option.Name}";
            }
        }
    }

    private static IEnumerable<MethodInfo> LiveTestMethods() =>
        typeof(CoverageTests).Assembly.GetTypes().SelectMany(type => type.GetMethods());

    private static IEnumerable<(string Path, Command Command)> Leaves(Command command, string path) =>
        command.Subcommands.Count == 0
            ? [(path, command)]
            : command.Subcommands.Where(child => !child.Hidden).SelectMany(child => Leaves(child, (path + " " + child.Name).Trim()));
}
