using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo connections</c>: <c>list</c> prints the ERP connections the signed-in user can use, from
/// <c>list_connections</c>, and <c>switch</c> sets the current profile's default connection in
/// <c>config.json</c>, from a picker on a terminal or from the name given.
/// </summary>
public static class ConnectionsCommands
{
    public const string SwitchUsage = "velixo connections switch NAME";

    private const string DefaultSuffix = " (default)";

    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new Command("connections", "The ERP connections the commands run against.") { BuildList(services), BuildSwitch(services) };
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
            "List the ERP connections you can use, with their type, status, company and address, and mark the one the commands use by default. "
            + "Example: velixo connections list --status ok")
        {
            status,
        };

        list.SetAction(async (parse, cancellationToken) =>
        {
            JsonArray all = await FetchAsync(services, cancellationToken).ConfigureAwait(false);
            var wanted = parse.GetValue(status);
            var connections = new JsonArray(all
                .Where(connection => string.IsNullOrEmpty(wanted)
                    || string.Equals(CommandText.String(connection?["status"]), wanted, StringComparison.OrdinalIgnoreCase))
                .Select(connection => connection?.DeepClone())
                .ToArray());

            MarkDefault(connections, services.Settings.Connection, services.Output.Format);
            if (services.Output.Format is OutputFormat.Table or OutputFormat.Csv)
            {
                connections = ShapeForColumns(connections);
            }

            services.Output.WriteResult(connections);
            return ExitCodes.Success;
        });

        return list;
    }

    private static Command BuildSwitch(CliServices services)
    {
        var name = new Argument<string?>("name")
        {
            Description = "The connection, as 'velixo connections list' names it. Without it, a terminal shows the list to pick from.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var clear = new Option<bool>("--clear") { Description = "Remove the profile's default connection." };
        var command = new Command(
            "switch",
            "Change the connection the commands use when no --connection is given: pick it from the list on a terminal, or name it. "
            + "Example: velixo connections switch gab-dev")
        {
            name,
            clear,
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var given = string.IsNullOrWhiteSpace(parse.GetValue(name)) ? null : parse.GetValue(name)!.Trim();
            var clearing = parse.GetValue(clear);
            if (clearing && given is not null)
            {
                throw CliException.Usage("Give a connection name or --clear, not both.", SwitchUsage);
            }

            var profile = services.Settings.ProfileName;
            CliConfig config = services.Profiles.LoadConfig();
            var current = config.Profiles.GetValueOrDefault(profile)?.Connection;

            string? chosen = null;
            if (!clearing)
            {
                JsonArray connections = await FetchAsync(services, cancellationToken).ConfigureAwait(false);
                chosen = given is null ? Pick(services, connections, current) : Find(connections, given);
                if (chosen is null)
                {
                    throw new CliException(ExitCodes.Interrupted, ErrorCodes.Interrupted, "No connection was chosen; the default is unchanged.");
                }
            }

            ConfigCommands.Write(config, $"profiles.{profile}.connection", chosen);
            services.Profiles.SaveConfig(config);

            if (chosen is not null && services.Settings.Connection is { } effective
                && !string.Equals(effective, current, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(effective, chosen, StringComparison.OrdinalIgnoreCase))
            {
                services.Output.Progress(
                    $"Warning: '{effective}' from --connection, VELIXO_CONNECTION or .velixo.json still comes before the profile's default here.");
            }

            services.Output.WriteResult(new JsonObject { ["profile"] = profile, ["connection"] = chosen });
            return ExitCodes.Success;
        });

        return command;
    }

    private static async Task<JsonArray> FetchAsync(CliServices services, CancellationToken cancellationToken)
    {
        McpToolResult result = await services.Mcp.CallToolAsync(McpToolNames.ListConnections, [], cancellationToken).ConfigureAwait(false);
        UnwrappedToolResult listing = ToolResultUnwrapper.Unwrap(result);
        CommandText.RelayWarnings(services.Output, listing.Document);
        return listing.Data as JsonArray ?? [];
    }

    /// <summary>Shows the picker with the current default selected, and returns the name picked, or null when cancelled.</summary>
    /// <exception cref="CliException">Exit 2 when there is no terminal to pick on; exit 5 when there is no connection to pick.</exception>
    private static string? Pick(CliServices services, JsonArray connections, string? current)
    {
        if (!services.Settings.IsInteractive || services.Console.IsErrorRedirected)
        {
            throw CliException.Usage(
                "Name the connection: without a terminal there is no list to pick from.",
                $"Run 'velixo connections list', then '{SwitchUsage}'.");
        }

        var rows = connections.OfType<JsonObject>()
            .Select(connection => (
                Name: CommandText.String(connection["name"]) ?? string.Empty,
                Type: CommandText.String(connection["type"]) ?? string.Empty,
                Status: CommandText.String(connection["status"]) ?? string.Empty))
            .Where(row => row.Name.Length > 0)
            .ToList();
        if (rows.Count == 0)
        {
            throw new CliException(
                ExitCodes.ServerRefused,
                ErrorCodes.ToolError,
                "There is no ERP connection to switch to.",
                "Add a connection in Velixo, then run 'velixo connections switch' again.");
        }

        var nameWidth = rows.Max(row => row.Name.Length);
        var typeWidth = rows.Max(row => row.Type.Length);
        var labels = rows
            .Select(row =>
            {
                var label = $"{row.Name.PadRight(nameWidth)}  {row.Type.PadRight(typeWidth)}  {row.Status}";
                return IsCurrent(row.Name, current) ? label + "  (current)" : label;
            })
            .ToList();
        var initial = Math.Max(0, rows.FindIndex(row => IsCurrent(row.Name, current)));

        var picked = Picker.Choose(
            services.Console,
            $"Default connection for profile '{services.Settings.ProfileName}'",
            labels,
            initial,
            services.Settings.Output.UseColor);
        return picked is { } index ? rows[index].Name : null;
    }

    /// <summary>Returns the name as the server spells it.</summary>
    /// <exception cref="CliException">Exit 2 when no connection has that name.</exception>
    private static string Find(JsonArray connections, string given) =>
        connections.OfType<JsonObject>()
            .Select(connection => CommandText.String(connection["name"]))
            .FirstOrDefault(name => IsCurrent(name, given))
        ?? throw CliException.Usage($"There is no connection named '{given}'.", "Run 'velixo connections list' for the names.");

    private static bool IsCurrent(string? name, string? current) =>
        name is not null && string.Equals(name, current, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gives every row the same columns, with <c>url</c> last, where its length pushes no other column off the screen.
    /// Drops <c>description</c> when it only repeats the type or the row has a <c>company</c>; from an older server,
    /// which sends neither field, it is the only detail beyond the type and stays.
    /// </summary>
    private static JsonArray ShapeForColumns(JsonArray connections)
    {
        var rows = connections.OfType<JsonObject>().ToList();
        foreach (JsonObject row in rows)
        {
            if (CommandText.String(row["company"]) is not null
                || string.Equals(CommandText.String(row["description"]), $"{CommandText.String(row["type"])} ERP connection", StringComparison.Ordinal))
            {
                row.Remove("description");
            }
        }

        var columns = rows.SelectMany(row => row.Select(property => property.Key)).Distinct(StringComparer.Ordinal).ToList();
        if (columns.Remove("url"))
        {
            columns.Add("url");
        }

        return new JsonArray(rows
            .Select(row => (JsonNode)new JsonObject(columns.Select(column => KeyValuePair.Create(column, row[column]?.DeepClone()))))
            .ToArray());
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
