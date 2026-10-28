namespace Velixo.Cli.Config;

/// <summary>
/// Where the CLI keeps its files: <c>config.json</c>, <c>credentials.json</c> and one
/// <c>state/&lt;profile&gt;.json</c> per profile, under one root directory.
/// </summary>
public sealed class ConfigPaths(string root)
{
    public const string DirectoryName = "velixo";

    public string Root { get; } = root;

    public string ConfigFile => Path.Combine(Root, "config.json");

    public string CredentialsFile => Path.Combine(Root, "credentials.json");

    public string StateDirectory => Path.Combine(Root, "state");

    public string StateFile(string profile) => Path.Combine(StateDirectory, ProfileNames.Validate(profile) + ".json");

    /// <summary>
    /// The root: <c>VELIXO_CONFIG_DIR</c> when set, else <c>%APPDATA%\velixo</c> on Windows, else
    /// <c>$XDG_CONFIG_HOME/velixo</c>, else <c>~/.config/velixo</c>.
    /// </summary>
    public static ConfigPaths Resolve(IEnvironmentReader environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var overridden = environment.GetVariable(EnvironmentVariables.ConfigDirectory);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return new ConfigPaths(Path.GetFullPath(overridden, environment.CurrentDirectory));
        }

        if (environment.IsWindows)
        {
            var appData = environment.GetVariable("APPDATA");
            return new ConfigPaths(Path.Combine(
                string.IsNullOrWhiteSpace(appData) ? Path.Combine(environment.HomeDirectory, "AppData", "Roaming") : appData,
                DirectoryName));
        }

        var xdg = environment.GetVariable("XDG_CONFIG_HOME");
        return new ConfigPaths(Path.Combine(
            string.IsNullOrWhiteSpace(xdg) ? Path.Combine(environment.HomeDirectory, ".config") : xdg,
            DirectoryName));
    }
}
