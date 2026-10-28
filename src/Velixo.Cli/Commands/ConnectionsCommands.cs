using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo connections</c>: <c>list</c> prints the ERP connections the signed-in user can use, from
/// <c>list_connections</c>, and <c>use</c> sets the current profile's default connection in <c>config.json</c>.
/// </summary>
public static class ConnectionsCommands
{
    private const string DefaultSuffix = " (default)";

    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new Command("connections", "The ERP connections the commands run against.") { BuildList(services), BuildUse(services) };
    }

    private static Command BuildList(CliServices services)
    {
        var status = new Option<string?>("--status")
        {
            Description = "Only the connections with this status, such as ok or no_credentials.",
            HelpName = "status",
        };
        var list = new Command(
            "list",
            "List the ERP connections you can use, with their type and status, and mark the one the commands use by default. "
            + "Example: velixo connections list --status ok")
        {
            status,
        };

        list.SetAction(async (parse, cancellationToken) =>
        {
            McpToolResult result = await services.Mcp.CallToolAsync(McpToolNames.ListConnections, [], cancellationToken).ConfigureAwait(false);
            UnwrappedToolResult listing = ToolResultUnwrapper.Unwrap(result);
            CommandText.RelayWarnings(services.Output, listing.Document);

            JsonArray all = listing.Data as JsonArray ?? [];
            var wanted = parse.GetValue(status);
            var connections = new JsonArray(all
                .Where(connection => string.IsNullOrEmpty(wanted)
                    || string.Equals(CommandText.String(connection?["status"]), wanted, StringComparison.OrdinalIgnoreCase))
                .Select(connection => connection?.DeepClone())
                .ToArray());

            MarkDefault(connections, services.Settings.Connection, services.Output.Format);
            services.Output.WriteResult(connections);
            return ExitCodes.Success;
        });

        return list;
    }

    private static Command BuildUse(CliServices services)
    {
        var name = new Argument<string?>("name")
        {
            Description = "The connection, as 'velixo connections list' names it.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var clear = new Option<bool>("--clear") { Description = "Remove the profile's default connection." };
        var use = new Command(
            "use",
            "Make a connection the default of the current profile, used when no --connection is given. Example: velixo connections use gab-dev")
        {
            name,
            clear,
        };

        use.SetAction(parse =>
        {
            var connection = string.IsNullOrWhiteSpace(parse.GetValue(name)) ? null : parse.GetValue(name)!.Trim();
            var clearing = parse.GetValue(clear);
            if (clearing == (connection is not null))
            {
                throw CliException.Usage(
                    clearing ? "Give a connection name or --clear, not both." : "Give the connection to use, or --clear to remove the default.",
                    "velixo connections use NAME");
            }

            var profile = services.Settings.ProfileName;
            CliConfig config = services.Profiles.LoadConfig();
            ConfigCommands.Write(config, $"profiles.{profile}.connection", connection);
            services.Profiles.SaveConfig(config);

            services.Output.WriteResult(new JsonObject { ["profile"] = profile, ["connection"] = connection });
            return ExitCodes.Success;
        });

        return use;
    }

    /// <summary>Marks the connection named <paramref name="defaultName"/>: a <c>(default)</c> suffix on its name in a table, <c>"default": true</c> otherwise.</summary>
    private static void MarkDefault(JsonArray connections, string? defaultName, OutputFormat format)
    {
        if (defaultName is null)
        {
            return;
        }

        foreach (JsonObject connection in connections.OfType<JsonObject>())
        {
            if (CommandText.String(connection["name"]) is not { } connectionName
                || !string.Equals(connectionName, defaultName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (format == OutputFormat.Table)
            {
                connection["name"] = connectionName + DefaultSuffix;
            }
            else
            {
                connection["default"] = true;
            }
        }
    }
}
