using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Auth;
using Velixo.Cli.Config;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary><c>velixo whoami</c>: who the profile is signed in as, from the stored claims, with no network call.</summary>
public static class WhoamiCommand
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var command = new Command("whoami", "Show who the profile is signed in as. Reads the stored sign-in; makes no network call.");
        command.SetAction(_ =>
        {
            ResolvedSettings settings = services.Settings;
            StoredCredentials credentials = services.Tokens.Get(settings.ProfileName) is { RefreshToken.Length: > 0 } stored
                ? stored
                : throw CliException.NotSignedIn(settings.ProfileName);

            var environment = credentials.McpUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
                ? Environments.NameOf(parsed) ?? url
                : settings.Target;

            JsonObject document = SignInRecords.Describe(credentials, settings.ProfileName, environment);
            document["mcpSessionId"] = services.Profiles.LoadState(settings.ProfileName).McpSessionId;

            services.Output.WriteResult(document);
            return ExitCodes.Success;
        });

        return command;
    }
}
