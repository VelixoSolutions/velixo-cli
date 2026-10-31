namespace Velixo.Cli.Config;

/// <summary>Everything settings are resolved from, gathered so the resolution is a pure function.</summary>
public sealed class SettingsSources
{
    required public CommandLineValues Flags { get; init; }

    required public Func<string, string?> GetVariable { get; init; }

    public ProjectConfig? Project { get; init; }

    required public CliConfig Config { get; init; }

    public bool IsOutputRedirected { get; init; }

    public bool IsErrorRedirected { get; init; }

    public bool IsInputRedirected { get; init; }
}
