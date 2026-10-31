namespace Velixo.Cli.Mcp;

/// <summary>
/// The Velixo MCP server's tool names, as <c>PublicToolNames</c> in <c>packages/mcp-server</c>
/// declares them.
/// </summary>
public static class McpToolNames
{
    public const string ListConnections = "list_connections";
    public const string ToolGuidance = "tool_guidance";
    public const string PrepareQuery = "prepare_query";
    public const string ExecuteQuery = "execute_query";
    public const string CreateWorkbookFromQuery = "create_workbook_from_query";
    public const string PrepareCommands = "prepare_commands";
    public const string ExecuteCommands = "execute_commands";
    public const string GetStatus = "get_status";
    public const string GetUploadUrl = "get_upload_url";
    public const string ListStagedFiles = "list_staged_files";
    public const string ListImportScenarios = "list_import_scenarios";
    public const string ExecuteImportScenario = "execute_import_scenario";
    public const string SendNotification = "send_notification";
    public const string SignOut = "sign_out";

    /// <summary>The argument that carries the caller's reason for a call.</summary>
    public const string IntentArgument = "intent";
}
