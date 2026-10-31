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
            Description = "Do not open a browser: print the sign-in URL. At a terminal, wait for the address the browser lands on; otherwise print the command that completes the sign-in.",
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
                if (PastedAddress(services, document) is { } address)
                {
                    document = await signIn.CompleteAsync(services.Settings, address, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Shows the sign-in URL and reads the address the browser lands on, so one command signs in on a machine
    /// without a browser. Returns null without asking when nobody can answer, a script, a pipe, an agent or JSON
    /// output, and when the person enters nothing: the URL and the <c>--complete</c> command are then printed.
    /// </summary>
    private static string? PastedAddress(CliServices services, JsonObject started)
    {
        if (!ValuePrompt.CanAsk(services) || services.Output.Format == OutputFormat.Json)
        {
            return null;
        }

        TextWriter prompt = services.Console.Error;
        prompt.Write(
            $"Open this URL in a browser and sign in:\n\n  {CommandText.String(started["browserUrl"])}\n\n"
            + "The browser then lands on a page that does not load. Copy its address.\n\n"
            + "Paste the address your browser lands on: ");
        prompt.Flush();

        var answer = services.Console.In.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(answer))
        {
            prompt.WriteLine();
            return null;
        }

        return answer;
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
