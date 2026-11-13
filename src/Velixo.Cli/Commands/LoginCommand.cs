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
                document = await WaitWithinTimeoutAsync(services, signIn, cancellationToken).ConfigureAwait(false);
            }
            else if (parse.GetValue(noBrowser) || !services.Settings.IsInteractive || NoBrowserHere(services))
            {
                document = await signIn.BeginDeviceSignInAsync(services.Settings, cancellationToken).ConfigureAwait(false);
                if (CanWaitHere(services))
                {
                    ShowCode(services, document);
                    document = await WaitWithinTimeoutAsync(services, signIn, cancellationToken).ConfigureAwait(false);
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
    /// Waits for the device sign-in for what is left of <c>--timeout</c>, the one budget of the whole command, and
    /// exits 124 when it runs out. The pending sign-in stays in the profile state, so <c>login --complete</c> can still
    /// finish it until its code expires.
    /// </summary>
    private static async Task<JsonObject> WaitWithinTimeoutAsync(CliServices services, GatewaySignIn signIn, CancellationToken cancellationToken)
    {
        if (services.FollowOptions().Timeout is not { } left)
        {
            return await signIn.WaitForDeviceSignInAsync(services.Settings, cancellationToken).ConfigureAwait(false);
        }

        using var deadline = new CancellationTokenSource(left, services.Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            return await signIn.WaitForDeviceSignInAsync(services.Settings, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new CliException(
                ExitCodes.Timeout,
                ErrorCodes.Timeout,
                $"The --timeout of {DurationText.Format(services.Settings.Timeout!.Value)} ran out before the sign-in was finished in the browser. "
                    + "The code stays good until it expires.",
                $"velixo login --profile {services.Settings.ProfileName} --complete");
        }
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

    /// <summary>
    /// Shows the code, even under <c>--quiet</c>, since the sign-in cannot go on without it. At a terminal that shows
    /// spinners, the spinner below it says what the command waits for.
    /// </summary>
    private static void ShowCode(CliServices services, JsonObject started)
    {
        TextWriter prompt = services.Console.Error;
        prompt.Write(services.Output.Settings.Animate
            ? $"{Instructions(started)}\n\n"
            : $"{Instructions(started)} {GatewaySignIn.WaitingForDeviceSignIn}\n");
        prompt.Flush();
    }

    /// <summary>
    /// Tells the person where to sign in: the short verification URL, and the code to type there. We intentionally
    /// do not show the URL that carries the code, because a short address is easy to type on another device.
    /// </summary>
    private static string Instructions(JsonObject started) =>
        $"Open this URL in a browser on any device:\n\n  {CommandText.String(started["verificationUri"])}\n\n"
        + $"Enter the code {CommandText.String(started["userCode"])}, and sign in.";

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
