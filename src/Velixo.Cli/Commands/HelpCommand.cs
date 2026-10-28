using System.CommandLine;
using System.CommandLine.Help;
using System.Text;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo help</c>: the help of one command, or with <c>--all</c> the whole command tree, which an
/// agent reads in one call to learn the surface.
/// </summary>
public static class HelpCommand
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var path = new Argument<string[]>("command")
        {
            Description = "The command to show help for, such as 'query build'.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var all = new Option<bool>("--all") { Description = "Print every command with its arguments and options. With --output json, as one document." };

        var command = new Command("help", "Show help. Example: velixo help --all --output json") { path, all };

        command.SetAction(parse =>
        {
            RootCommand root = (RootCommand)parse.RootCommandResult.Command;

            if (parse.GetValue(all))
            {
                JsonObject tree = Describe(root);
                if (services.Output.Format == OutputFormat.Json)
                {
                    services.Output.WriteJson(tree);
                }
                else
                {
                    services.Output.WriteText(DescribeAsText(tree));
                }

                return ExitCodes.Success;
            }

            string[] words = [.. parse.GetValue(path) ?? [], "--help"];
            return root.Parse(words).Invoke(new InvocationConfiguration
            {
                Output = services.Console.Out,
                Error = services.Console.Error,
            });
        });

        return command;
    }

    /// <summary>The command tree under <paramref name="root"/> as JSON: names, descriptions, arguments and options.</summary>
    public static JsonObject Describe(RootCommand root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return new JsonObject
        {
            ["name"] = "velixo",
            ["version"] = CliInfo.Version,
            ["description"] = root.Description,
            ["globalOptions"] = new JsonArray(root.Options.Where(IsDocumented).Select(DescribeOption).ToArray()),
            ["commands"] = new JsonArray(root.Subcommands.Select(child => (JsonNode?)DescribeCommand(child, "velixo")).ToArray()),
        };
    }

    private static JsonObject DescribeCommand(Command command, string parentPath)
    {
        var path = $"{parentPath} {command.Name}";
        var described = new JsonObject
        {
            ["name"] = command.Name,
            ["path"] = path,
            ["description"] = command.Description,
            ["arguments"] = new JsonArray(command.Arguments.Where(argument => !argument.Hidden).Select(DescribeArgument).ToArray()),
            ["options"] = new JsonArray(command.Options.Where(IsDocumented).Select(DescribeOption).ToArray()),
        };

        if (command.Subcommands.Count > 0)
        {
            described["commands"] = new JsonArray(command.Subcommands.Select(child => (JsonNode?)DescribeCommand(child, path)).ToArray());
        }

        return described;
    }

    private static bool IsDocumented(Option option) => !option.Hidden && option is not HelpOption and not VersionOption;

    private static JsonNode? DescribeArgument(Argument argument) => new JsonObject
    {
        ["name"] = argument.Name,
        ["description"] = argument.Description,
        ["required"] = argument.Arity.MinimumNumberOfValues > 0,
        ["repeatable"] = argument.Arity.MaximumNumberOfValues > 1,
    };

    private static JsonNode? DescribeOption(Option option)
    {
        var isFlag = option.ValueType == typeof(bool);
        var described = new JsonObject
        {
            ["name"] = option.Name,
            ["aliases"] = new JsonArray(option.Aliases.Order(StringComparer.Ordinal).Select(alias => (JsonNode?)alias).ToArray()),
            ["description"] = option.Description,
            ["required"] = option.Required,
            ["flag"] = isFlag,
        };

        if (!isFlag)
        {
            described["valueName"] = option.HelpName;
            described["repeatable"] = option.Arity.MaximumNumberOfValues > 1;
        }

        return described;
    }

    private static string DescribeAsText(JsonObject tree)
    {
        var text = new StringBuilder();
        text.Append(CommandText.String(tree["description"])).Append("\n\nGlobal options:\n");
        foreach (JsonNode? option in tree["globalOptions"]!.AsArray())
        {
            text.Append("  ").Append(OptionUsage(option)).Append("\n      ").Append(CommandText.String(option?["description"])).Append('\n');
        }

        text.Append("\nCommands:\n");
        foreach (JsonNode? command in tree["commands"]!.AsArray())
        {
            AppendCommand(text, command);
        }

        return text.ToString();
    }

    private static void AppendCommand(StringBuilder text, JsonNode? command)
    {
        var usage = new StringBuilder(CommandText.String(command?["path"]));
        foreach (JsonNode? argument in command?["arguments"]?.AsArray() ?? [])
        {
            var name = CommandText.String(argument?["name"]);
            var repeat = argument?["repeatable"]?.GetValue<bool>() == true ? "..." : string.Empty;
            usage.Append(argument?["required"]?.GetValue<bool>() == true ? $" <{name}>{repeat}" : $" [<{name}>{repeat}]");
        }

        text.Append('\n').Append(usage).Append("\n    ").Append(CommandText.String(command?["description"])).Append('\n');
        foreach (JsonNode? option in command?["options"]?.AsArray() ?? [])
        {
            text.Append("    ").Append(OptionUsage(option)).Append("  ").Append(CommandText.String(option?["description"])).Append('\n');
        }

        foreach (JsonNode? child in command?["commands"]?.AsArray() ?? [])
        {
            AppendCommand(text, child);
        }
    }

    private static string OptionUsage(JsonNode? option)
    {
        IEnumerable<string?> aliases = option?["aliases"]?.AsArray().Select(CommandText.String) ?? [];
        var names = string.Join(", ", aliases.Append(CommandText.String(option?["name"])));
        return option?["flag"]?.GetValue<bool>() == true ? names : $"{names} <{CommandText.String(option?["valueName"]) ?? "value"}>";
    }
}
