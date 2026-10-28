using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Velixo.Cli.Commands;
using Velixo.Cli.Output;

namespace Velixo.Cli;

public static class Program
{
    public const string Description =
        "The Velixo CLI: query your ERP, run screen commands, move files and run imports from a shell. "
        + "Start with 'velixo login', then 'velixo guide'.";

    /// <summary>
    /// How every command line is parsed. Response files are off: the parser would otherwise read a
    /// value that starts with <c>@</c>, such as an address after <c>notify --target</c>, as a file whose
    /// words it splices into the command line.
    /// </summary>
    public static ParserConfiguration ParserConfiguration { get; } = new() { ResponseFileTokenReplacer = null };

    public static Task<int> Main(string[] args) => RunAsync(args, CliServices.CreateDefault());

    /// <summary>
    /// The command tree. Each command group lives in its own file under <c>Commands/</c> and is added
    /// here once, so adding a group touches this list and nothing else.
    /// </summary>
    public static RootCommand BuildRootCommand(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var root = new RootCommand(Description);
        foreach (Option option in services.Options.All)
        {
            root.Options.Add(option);
        }

        Command[] commands =
        [
            LoginCommand.Build(services),
            LogoutCommand.Build(services),
            WhoamiCommand.Build(services),
            StatusCommand.Build(services),
            AuthCommands.Build(services),
            ConnectionsCommands.Build(services),
            GuideCommand.Build(services),
            QueryCommands.Build(services),
            ScreenCommands.Build(services),
            TaskCommands.Build(services),
            FilesCommands.Build(services),
            ImportCommands.Build(services),
            NotifyCommand.Build(services),
            ToolsCommands.Build(services),
            HelpCommand.Build(services),
            ConfigCommands.Build(services),
            VersionCommand.Build(services),
        ];

        foreach (Command command in commands)
        {
            root.Subcommands.Add(command);
        }

        return root;
    }

    /// <summary>Parses <paramref name="args"/>, runs the command, and maps every failure to its exit code and envelope.</summary>
    public static async Task<int> RunAsync(string[] args, CliServices services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);

        RootCommand root = BuildRootCommand(services);
        ParseResult parse = root.Parse(args, ParserConfiguration);

        try
        {
            services.Bind(parse);
        }
        catch (CliException exception)
        {
            services.BindFallbackOutput().WriteError(exception);
            return exception.ExitCode;
        }

        if (args.Length == 0)
        {
            // A person at a terminal gets the welcome screen; a pipe, a script or an agent gets the help
            // text, which is what they can parse.
            if (services.Settings.IsInteractive && !services.Console.IsOutputRedirected)
            {
                return WelcomeScreen.Show(services);
            }

            parse = root.Parse(["--help"], ParserConfiguration);
        }

        if (parse.Errors.Count > 0 && parse.Action is ParseErrorAction)
        {
            var path = CommandPath(parse.CommandResult);
            var usage = CliException.Usage(
                string.Join(" ", parse.Errors.Select(error => error.Message)),
                $"{path} --help");
            services.Output.WriteError(usage);
            return usage.ExitCode;
        }

        var configuration = new InvocationConfiguration
        {
            EnableDefaultExceptionHandler = false,
            Output = services.Console.Out,
            Error = services.Console.Error,
        };

        try
        {
            return await parse.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (CliException exception)
        {
            services.Output.WriteError(exception);
            return exception.ExitCode;
        }
        catch (OperationCanceledException)
        {
            var interrupted = new CliException(
                ExitCodes.Interrupted,
                ErrorCodes.Interrupted,
                "Interrupted. A task the command started keeps running on the server.");
            services.Output.WriteError(interrupted);
            return interrupted.ExitCode;
        }
        catch (Exception exception)
        {
            var failure = new CliException(ExitCodes.Failure, ErrorCodes.Internal, exception.Message, "Retry once; if it fails again, report it.");
            services.Output.WriteError(failure);
            return failure.ExitCode;
        }
    }

    /// <summary>The command line that names <paramref name="result"/>'s command, such as <c>velixo query build</c>.</summary>
    public static string CommandPath(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var names = new List<string>();
        for (SymbolResult? current = result; current is not null; current = current.Parent)
        {
            if (current is CommandResult command)
            {
                names.Insert(0, command.Parent is null ? "velixo" : command.Command.Name);
            }
        }

        return string.Join(" ", names);
    }
}
