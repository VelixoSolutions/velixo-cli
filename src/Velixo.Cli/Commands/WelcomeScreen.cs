using Velixo.Cli.Auth;
using Velixo.Cli.Config;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// What a person sees on a bare <c>velixo</c> in a terminal: the mark, the version, whether the profile
/// is signed in, and the four commands that start the work. Reads only the stored sign-in; no network.
/// </summary>
public static class WelcomeScreen
{
    public const string DocumentationUrl = "https://github.com/VelixoSolutions/velixo-cli#readme";

    public static int Show(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        ResolvedSettings settings = services.Settings;
        var useColor = settings.Output.UseColor;
        TextWriter output = services.Console.Out;

        output.WriteLine();
        output.WriteLine(Indent(VelixoLogo.Render(useColor, services.Console.WindowWidth)));
        output.WriteLine();
        output.WriteLine($"  {Bold("Velixo CLI " + CliInfo.Version, useColor)}  the Velixo MCP server from your shell");
        output.WriteLine();
        output.WriteLine("  " + SignInLine(services, settings));
        output.WriteLine();
        output.WriteLine("  Get started");
        output.WriteLine("    velixo login                Sign in to Velixo");
        output.WriteLine("    velixo connections list     The ERP connections you can use");
        output.WriteLine("    velixo guide                The rules for building and running queries");
        output.WriteLine("    velixo --help               Every command");
        output.WriteLine();
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
