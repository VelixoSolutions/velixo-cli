namespace Velixo.Cli.Config;

/// <summary>The environment variables the CLI reads.</summary>
public static class EnvironmentVariables
{
    public const string Profile = "VELIXO_PROFILE";
    public const string Environment = "VELIXO_ENV";
    /// <summary>The URL of the Velixo server, as <c>--url</c> gives it.</summary>
    public const string Url = "VELIXO_URL";

    /// <summary>The first name of <see cref="Url"/>, still read when <see cref="Url"/> is not set.</summary>
    public const string McpUrl = "VELIXO_MCP_URL";
    public const string Connection = "VELIXO_CONNECTION";
    public const string Intent = "VELIXO_INTENT";
    public const string Token = "VELIXO_TOKEN";
    public const string Output = "VELIXO_OUTPUT";
    public const string ConfigDirectory = "VELIXO_CONFIG_DIR";

    /// <summary>The OAuth client id a <see cref="Token"/> was issued to, for a machine that never signed in itself.</summary>
    public const string ClientId = "VELIXO_CLIENT_ID";

    public const string NoColor = "NO_COLOR";
}
