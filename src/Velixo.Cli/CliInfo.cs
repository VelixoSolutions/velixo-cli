using System.Reflection;

namespace Velixo.Cli;

/// <summary>The CLI's own version, from the assembly's informational version.</summary>
public static class CliInfo
{
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(CliInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
        {
            return typeof(CliInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
