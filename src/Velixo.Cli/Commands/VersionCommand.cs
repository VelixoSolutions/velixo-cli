using System.Net;
using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using Velixo.Cli.Serialization;

namespace Velixo.Cli.Commands;

/// <summary><c>velixo version</c>: the CLI's version, and with <c>--check</c> the latest published release.</summary>
public static class VersionCommand
{
    public const string ReleasesUrl = "https://api.github.com/repos/VelixoSolutions/velixo-cli/releases/latest";

    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var check = new Option<bool>("--check") { Description = "Also look up the latest release on GitHub." };
        var command = new Command("version", "Print the CLI's version.") { check };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var document = new JsonObject
            {
                ["version"] = CliInfo.Version,
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["platform"] = RuntimeInformation.RuntimeIdentifier,
            };

            if (parse.GetValue(check))
            {
                await AddLatestReleaseAsync(services, document, cancellationToken).ConfigureAwait(false);
            }

            if (services.Output.Format == OutputFormat.Json)
            {
                services.Output.WriteJson(document);
            }
            else
            {
                services.Output.WriteText($"velixo {CliInfo.Version}");
                if (document["latest"] is not null || document["note"] is not null)
                {
                    services.Output.WriteText(CommandText.String(document["note"]) ?? $"latest release: {CommandText.String(document["latest"])}");
                }
            }

            return ExitCodes.Success;
        });

        return command;
    }

    public static bool IsNewer(string latest, string current) =>
        Version.TryParse(latest.TrimStart('v', 'V').Split('-', '+')[0], out Version? latestVersion)
        && Version.TryParse(current.Split('-', '+')[0], out Version? currentVersion)
        && latestVersion > currentVersion;

    private static async Task AddLatestReleaseAsync(CliServices services, JsonObject document, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using HttpResponseMessage response = await HttpCalls.SendAsync(services.Http, request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            document["latest"] = null;
            document["updateAvailable"] = false;
            document["note"] = "No release of the Velixo CLI is published yet.";
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.Http,
                $"GitHub answered HTTP {(int)response.StatusCode} to the release lookup.",
                "Retry later.");
        }

        GitHubRelease release = await HttpCalls
            .ReadJsonAsync(response, CliJsonContext.Default.GitHubRelease, "The release", cancellationToken)
            .ConfigureAwait(false);
        var latest = release.TagName ?? string.Empty;

        document["latest"] = latest.TrimStart('v', 'V');
        document["updateAvailable"] = IsNewer(latest, CliInfo.Version);
        document["releaseUrl"] = release.HtmlUrl;
    }
}
