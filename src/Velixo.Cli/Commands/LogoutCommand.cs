using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Auth;
using Velixo.Cli.Config;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo logout</c>: ends the gateway session with <c>sign_out</c>, revokes the refresh token, and
/// deletes the profile's tokens and state.
/// </summary>
public static class LogoutCommand
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var all = new Option<bool>("--all") { Description = "Sign out of every profile." };
        var command = new Command("logout", "Sign out: end the Velixo session and delete the stored tokens and state.") { all };

        command.SetAction(async (parse, cancellationToken) =>
        {
            ResolvedSettings settings = services.Settings;
            IReadOnlyList<string> profiles = parse.GetValue(all)
                ? services.Tokens.ListProfiles()
                : [settings.ProfileName];

            foreach (var profile in profiles)
            {
                await SignOutAsync(services, profile, cancellationToken).ConfigureAwait(false);
            }

            JsonObject document = parse.GetValue(all)
                ? new JsonObject { ["signedOut"] = true, ["profiles"] = new JsonArray(profiles.Select(name => (JsonNode?)name).ToArray()) }
                : new JsonObject { ["signedOut"] = true, ["profile"] = settings.ProfileName };

            services.Output.WriteResult(document);
            return ExitCodes.Success;
        });

        return command;
    }

    private static async Task SignOutAsync(CliServices services, string profile, CancellationToken cancellationToken)
    {
        StoredCredentials? credentials = services.Tokens.Get(profile);
        if (credentials?.RefreshToken is { Length: > 0 } refreshToken)
        {
            try
            {
                IMcpClient mcp = ClientFor(services, profile, credentials);
                McpToolResult result = await mcp.CallToolAsync(McpToolNames.SignOut, [], cancellationToken).ConfigureAwait(false);
                _ = ToolResultUnwrapper.Unwrap(result);
            }
            catch (CliException exception)
            {
                services.Output.Warn($"The server did not confirm the sign-out of '{profile}': {exception.Message}");
            }

            if (credentials.RevocationEndpoint is { } revocation && credentials.ClientId is { } clientId)
            {
                try
                {
                    var latest = services.Tokens.Get(profile)?.RefreshToken ?? refreshToken;
                    await new OAuthTokenClient(services.Http).RevokeAsync(revocation, clientId, latest, cancellationToken).ConfigureAwait(false);
                }
                catch (CliException exception)
                {
                    services.Output.Warn($"The refresh token of '{profile}' could not be revoked: {exception.Message}");
                }
            }
        }

        // A refresh running in another process would otherwise write the profile's tokens back after the sign-out.
        using (await services.Tokens.LockForRefreshAsync(profile, cancellationToken).ConfigureAwait(false))
        {
            services.Tokens.Delete(profile);
        }

        services.Profiles.DeleteState(profile);
    }

    private static IMcpClient ClientFor(CliServices services, string profile, StoredCredentials credentials)
    {
        if (profile == services.Settings.ProfileName)
        {
            return services.Mcp;
        }

        Uri url = Uri.TryCreate(credentials.McpUrl, UriKind.Absolute, out Uri? stored) ? stored : services.Settings.McpUrl;
        ResolvedSettings settings = services.Settings with
        {
            ProfileName = profile,
            McpUrl = url,
            EnvironmentName = Environments.NameOf(url),
            EnvironmentToken = null,
        };
        var tokens = new GatewayTokenProvider(
            services.Tokens,
            new OAuthTokenClient(services.Http),
            new OAuthDiscovery(services.Http),
            settings,
            services.Clock);

        return new StreamableHttpMcpClient(services.Http, url, tokens, services.Profiles, profile, settings.Agent, CliInfo.Version);
    }
}
