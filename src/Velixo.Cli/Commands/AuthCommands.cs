using System.CommandLine;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary><c>velixo auth</c>: credentials for scripts.</summary>
public static class AuthCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var token = new Command(
            "token",
            "Print the profile's refresh token and nothing else, for VELIXO_TOKEN in a job that cannot sign in. "
            + "The gateway rotates it on every use, so jobs that run in parallel need one token each.");
        token.SetAction(async (_, cancellationToken) =>
        {
            var refreshToken = await services.AccessTokens.GetRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            services.Console.Out.Write(refreshToken);
            services.Console.Out.Write('\n');
            return ExitCodes.Success;
        });

        return new Command("auth", "Credentials for scripts and CI.") { token };
    }
}
