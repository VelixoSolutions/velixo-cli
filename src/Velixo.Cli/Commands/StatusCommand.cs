using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo status</c>: whether the server answers, whether the profile is signed in, and what it can
/// reach. It prints its document whatever the outcome and says what is wrong in <c>problems</c>.
/// </summary>
public static class StatusCommand
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var command = new Command(
            "status",
            "Check the setup: the server, the sign-in, the tools and the connections. Exit 0 when all work, 4 when sign-in is needed, 1 otherwise.");

        command.SetAction(async (_, cancellationToken) =>
        {
            ResolvedSettings settings = services.Settings;
            var problems = new JsonArray();
            var reachable = await IsReachableAsync(services, settings.McpUrl, problems, cancellationToken).ConfigureAwait(false);
            var signedIn = settings.EnvironmentToken is not null || services.Tokens.Get(settings.ProfileName)?.RefreshToken is { Length: > 0 };
            var authenticationProblem = !signedIn;
            int? toolCount = null;
            JsonNode? connections = null;

            if (!signedIn)
            {
                problems.Add((JsonNode)$"Not signed in on the profile '{settings.ProfileName}': run 'velixo login --profile {settings.ProfileName}'.");
            }
            else if (reachable)
            {
                try
                {
                    toolCount = (await services.Mcp.ListToolsAsync(cancellationToken).ConfigureAwait(false)).Count;
                    UnwrappedToolResult listing = ToolResultUnwrapper.Unwrap(
                        await services.Mcp.CallToolAsync(McpToolNames.ListConnections, [], cancellationToken).ConfigureAwait(false));
                    connections = listing.Data?.DeepClone();
                    CommandText.RelayWarnings(services.Output, listing.Document);
                }
                catch (CliException exception)
                {
                    problems.Add((JsonNode)exception.Message);
                    if (exception.ExitCode == ExitCodes.AuthenticationRequired)
                    {
                        signedIn = false;
                        authenticationProblem = true;
                    }
                }
            }

            var document = new JsonObject
            {
                ["server"] = new JsonObject
                {
                    ["url"] = settings.McpUrl.ToString(),
                    ["reachable"] = reachable,
                    ["version"] = signedIn && reachable ? services.Mcp.ServerInfo?.Version : null,
                },
                ["profile"] = settings.ProfileName,
                ["signedIn"] = signedIn,
                ["tools"] = toolCount,
                ["connections"] = connections ?? new JsonArray(),
                ["problems"] = problems,
            };

            services.Output.WriteResult(document);

            return !reachable ? ExitCodes.Failure
                : authenticationProblem ? ExitCodes.AuthenticationRequired
                : problems.Count > 0 ? ExitCodes.Failure
                : ExitCodes.Success;
        });

        return command;
    }

    private static async Task<bool> IsReachableAsync(CliServices services, Uri mcpUrl, JsonArray problems, CancellationToken cancellationToken)
    {
        var probe = new Uri(mcpUrl.GetLeftPart(UriPartial.Authority) + "/.well-known/oauth-protected-resource");
        using var request = new HttpRequestMessage(HttpMethod.Get, probe);
        try
        {
            using HttpResponseMessage response = await HttpCalls.SendAsync(services.Http, request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode >= 500)
            {
                problems.Add((JsonNode)$"{probe.Host} answered HTTP {(int)response.StatusCode}.");
                return false;
            }

            return true;
        }
        catch (CliException exception)
        {
            problems.Add((JsonNode)exception.Message);
            return false;
        }
    }
}
