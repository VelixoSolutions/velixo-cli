namespace Velixo.Cli.Config;

/// <summary>The process environment the CLI reads its settings from, so tests can supply their own.</summary>
public interface IEnvironmentReader
{
    string? GetVariable(string name);

    string CurrentDirectory { get; }

    string HomeDirectory { get; }

    bool IsWindows { get; }
}
