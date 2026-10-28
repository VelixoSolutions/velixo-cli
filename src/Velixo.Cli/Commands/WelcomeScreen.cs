using Velixo.Cli.Auth;
using Velixo.Cli.Config;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// What a person sees on a bare <c>velixo</c> in a terminal: the mark, the version, whether the profile
/// is signed in, and every command, grouped, with a one-line summary. Reads only the stored sign-in; no network.
/// </summary>
public static class WelcomeScreen
{
    public const string DocumentationUrl = "https://github.com/VelixoSolutions/velixo-cli#readme";

    /// <summary>
    /// Every command a person can run, in the order of the work, with the summary the screen shows. A
    /// test holds this list to the command tree, so a new command fails the build until it is added here.
    /// </summary>
    public static readonly IReadOnlyList<(string Group, IReadOnlyList<(string Command, string Summary)> Commands)> Groups =
    [
        ("Sign in", [
            ("login", "Sign in to Velixo (prod unless --env names another)"),
            ("logout", "Sign out and delete the stored sign-in"),
            ("whoami", "Who the profile is signed in as"),
            ("status", "Check the server, the sign-in, the tools and the connections"),
            ("auth token", "Print a refresh token for a job that cannot sign in"),
        ]),
        ("Connections", [
            ("connections list", "The ERP connections you can use"),
            ("connections switch", "Change the default connection"),
        ]),
        ("Queries", [
            ("query build", "Turn a question into a validated query plan"),
            ("query run", "Run a plan, a query or a saved inquiry"),
            ("query workbook", "Create a Velixo workbook from a query"),
            ("query list", "The queries you built, by number"),
            ("query delete", "Delete a query from the list"),
        ]),
        ("ERP commands", [
            ("command build", "Turn a request into a batch of screen commands"),
            ("command run", "Run a batch: reads, reports and writes"),
            ("command list", "The batches you built, by number"),
            ("command delete", "Delete a batch from the list"),
            ("task status", "Follow a long-running task"),
        ]),
        ("Files and imports", [
            ("files stage", "Stage a file to attach or import by its token"),
            ("files download", "Download reports, attachments and workbooks"),
            ("import scenarios", "The import scenarios and their columns"),
            ("import run", "Validate and run an import from a file"),
        ]),
        ("Miscellaneous", [
            ("notify", "Send a notification to the portal, email, Teams or Slack"),
        ]),
        ("Setup and reference", [
            ("guide", "How to use the commands"),
            ("config list", "Print the whole configuration"),
            ("config get", "Print one setting"),
            ("config set", "Change one setting"),
            ("config path", "Where the configuration, sign-in and lists are kept"),
            ("tools list", "The server's tools and their schemas"),
            ("tools guidance", "The server's own guidance for its tools"),
            ("help", "Help for any command: velixo help COMMAND"),
            ("version", "The CLI's version"),
        ]),
    ];

    public static int Show(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        ResolvedSettings settings = services.Settings;
        var useColor = settings.Output.UseColor;
        TextWriter output = services.Console.Out;

        output.WriteLine();
        output.WriteLine(Indent(VelixoLogo.Render(useColor, services.Console.WindowWidth)));
        output.WriteLine();
        output.WriteLine($"  {Bold("Velixo CLI " + CliInfo.Version, useColor)}  your ERP from the shell, with Velixo");
        output.WriteLine();
        output.WriteLine("  " + SignInLine(services, settings));
        output.WriteLine();
        var width = Groups.SelectMany(group => group.Commands).Max(entry => entry.Command.Length) + "velixo ".Length + 2;
        foreach ((var group, var commands) in Groups)
        {
            output.WriteLine("  " + Bold(group, useColor));
            foreach ((var command, var summary) in commands)
            {
                output.WriteLine($"    {("velixo " + command).PadRight(width)}{summary}");
            }

            output.WriteLine();
        }

        output.WriteLine("  Run velixo guide for the workflow, and add --help to any command for its flags.");
        output.WriteLine($"  Docs: {DocumentationUrl}");
        output.WriteLine();

        return ExitCodes.Success;
    }

    private static string SignInLine(CliServices services, ResolvedSettings settings)
    {
        StoredCredentials? credentials = services.Tokens.Get(settings.ProfileName);
        if (credentials is not { RefreshToken.Length: > 0 })
        {
            return $"Not signed in on profile '{settings.ProfileName}'. Run: velixo login";
        }

        var who = credentials.User?.Email ?? "a Velixo user";
        return $"Signed in as {who} on {settings.Target} (profile '{settings.ProfileName}')";
    }

    private static string Indent(string block) =>
        string.Join('\n', block.Split('\n').Select(line => "  " + line));

    private static string Bold(string text, bool useColor) => useColor ? $"\u001b[1m{text}\u001b[0m" : text;
}
