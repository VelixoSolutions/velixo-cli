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
            Description = "Do not open a browser here: print a URL and a code to enter in a browser on any device. At a terminal, wait for the sign-in; otherwise print the command that waits for it.",
        };
        var complete = new Option<bool>("--complete")
        {
            Description = "Wait for the sign-in that --no-browser started to be finished in the browser.",
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
            if (parse.GetValue(complete))
            {
                document = await signIn.WaitForDeviceSignInAsync(services.Settings, cancellationToken).ConfigureAwait(false);
            }
            else if (parse.GetValue(noBrowser) || !services.Settings.IsInteractive || NoBrowserHere(services))
            {
                document = await signIn.BeginDeviceSignInAsync(services.Settings, cancellationToken).ConfigureAwait(false);
                if (CanWaitHere(services))
                {
                    ShowCode(services.Console.Error, document);
                    document = await signIn.WaitForDeviceSignInAsync(services.Settings, cancellationToken).ConfigureAwait(false);
                }
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

    /// <summary>Tells, at a terminal, that no browser can open here and the sign-in goes on without one.</summary>
    private static bool NoBrowserHere(CliServices services)
    {
        if (services.Browser.UnavailableReason is not { } reason)
        {
            return false;
        }

        services.Output.Progress($"No browser can be opened here: {reason}. Signing in without one, as --no-browser does.");
        return true;
    }

    /// <summary>
    /// Tells whether this command can show the code to a person and wait for them: stdin and stderr are a terminal
    /// and no agent runs it. Stdout may be redirected, which only turns the final document into JSON. A script or an
    /// agent gets the <c>authorization_required</c> document and the command that waits instead.
    /// </summary>
    private static bool CanWaitHere(CliServices services) => ValuePrompt.CanAsk(services);

    private static void ShowCode(TextWriter prompt, JsonObject started)
    {
        prompt.Write($"{Instructions(started)} Waiting for the sign-in...\n");
        prompt.Flush();
    }

    /// <summary>
    /// Tells the person where to sign in. A URL that already carries the code needs no typing, so the person only
    /// checks that the page shows the same code, which stops them approving a sign-in someone else started.
    /// </summary>
    private static string Instructions(JsonObject started)
    {
        var code = CommandText.String(started["userCode"]);
        return CommandText.String(started["verificationUriComplete"]) is { } complete
            ? $"Open this URL in a browser on any device:\n\n  {complete}\n\nCheck that the page shows the code {code}, and sign in."
            : $"Open this URL in a browser on any device:\n\n  {CommandText.String(started["verificationUri"])}\n\nEnter the code {code}, and sign in.";
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
                $"{Instructions(document)} Then run this command, which waits for the sign-in:\n\n"
                + $"  {CommandText.String(document["nextStep"])}\n");
            return;
        }

        output.WriteText(
            $"Signed in as {CommandText.String(document["user"]?["email"]) ?? "an unknown user"}"
            + $" in organization {CommandText.String(document["organization"]?["name"]) ?? CommandText.String(document["organization"]?["id"]) ?? "unknown"}"
            + $" on {CommandText.String(document["environment"])} (profile {CommandText.String(document["profile"])}).");
    }
}
