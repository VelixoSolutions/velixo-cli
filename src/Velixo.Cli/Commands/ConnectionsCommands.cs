using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary><c>velixo connections list</c>: the ERP connections the signed-in user can use, from <c>list_connections</c>.</summary>
public static class ConnectionsCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var status = new Option<string?>("--status")
        {
            Description = "Only the connections with this status, such as ok or no_credentials.",
            HelpName = "status",
        };
        var list = new Command("list", "List the ERP connections you can use, with their type and status. Example: velixo connections list --status ok")
        {
            status,
        };

        list.SetAction(async (parse, cancellationToken) =>
        {
            McpToolResult result = await services.Mcp.CallToolAsync(McpToolNames.ListConnections, [], cancellationToken).ConfigureAwait(false);
            UnwrappedToolResult listing = ToolResultUnwrapper.Unwrap(result);
            CommandText.RelayWarnings(services.Output, listing.Document);

            JsonArray connections = listing.Data as JsonArray ?? [];
            if (parse.GetValue(status) is { Length: > 0 } wanted)
            {
                connections = new JsonArray(connections
                    .Where(connection => string.Equals(CommandText.String(connection?["status"]), wanted, StringComparison.OrdinalIgnoreCase))
                    .Select(connection => connection?.DeepClone())
                    .ToArray());
            }
            else
            {
                connections = (JsonArray)connections.DeepClone();
            }

            services.Output.WriteResult(connections);
            return ExitCodes.Success;
        });

        return new Command("connections", "The ERP connections the commands run against.") { list };
    }
}
