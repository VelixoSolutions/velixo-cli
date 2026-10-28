using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Auth;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary><c>velixo login</c>: signs a profile in through the Velixo OAuth gateway.</summary>
public static class LoginCommand
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var noBrowser = new Option<bool>("--no-browser")
        {
            Description = "Do not open a browser: print the sign-in URL and the command that completes the sign-in.",
        };
        var complete = new Option<string?>("--complete")
        {
            Description = "Finish a sign-in started with --no-browser, from the URL the browser landed on or the code in it.",
            HelpName = "url-or-code",
        };

        var command = new Command(
            "login",
            "Sign in to Velixo, on prod unless --env names another environment. Examples: velixo login, velixo login --env nx-staging")
        {
            noBrowser,
            complete,
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var signIn = new GatewaySignIn(services.Http, services.HttpWithoutRedirects, services.Tokens, services.Profiles, services.Browser, services.Clock, services.Output);

            JsonObject document;
            if (parse.GetValue(complete) is { } pasted)
            {
                document = await signIn.CompleteAsync(services.Settings, pasted, cancellationToken).ConfigureAwait(false);
            }
            else if (parse.GetValue(noBrowser) || !services.Settings.IsInteractive)
            {
                document = await signIn.BeginDeferredAsync(services.Settings, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                document = await signIn.SignInWithBrowserAsync(services.Settings, cancellationToken).ConfigureAwait(false);
            }

            Write(services.Output, document);
            return ExitCodes.Success;
        });

        return command;
    }

    private static void Write(OutputWriter output, JsonObject document)
    {
        if (output.Format == OutputFormat.Json)
        {
            output.WriteJson(document);
            return;
        }

        if (CommandText.String(document["status"]) == "authorization_required")
        {
            output.WriteText(
                $"Open this URL in a browser and sign in:\n\n  {CommandText.String(document["browserUrl"])}\n\n"
                + "The browser then lands on a page that does not load. Copy its address and run:\n\n"
                + $"  {CommandText.String(document["nextStep"])}\n");
            return;
        }

        output.WriteText(
            $"Signed in as {CommandText.String(document["user"]?["email"]) ?? "an unknown user"}"
            + $" in organization {CommandText.String(document["organization"]?["name"]) ?? CommandText.String(document["organization"]?["id"]) ?? "unknown"}"
            + $" on {CommandText.String(document["environment"])} (profile {CommandText.String(document["profile"])}).");
    }
}
