using Velixo.Cli.Output;

namespace Velixo.Cli.Config;

/// <summary>
/// Resolves each setting from the first source that has it: the command-line flag, the environment
/// variable, <c>.velixo.json</c>, the profile in <c>config.json</c>, then the built-in default.
/// </summary>
public static class SettingsResolver
{
    public static ResolvedSettings Resolve(SettingsSources sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        CommandLineValues flags = sources.Flags;
        string? Variable(string name) => NullIfBlank(sources.GetVariable(name));
        ProjectConfig project = sources.Project ?? new ProjectConfig();

        var profileName = FirstOf(flags.Profile, Variable(EnvironmentVariables.Profile), project.Profile)
            ?? ProfileNameFromTarget(flags, Variable, project)
            ?? NullIfBlank(sources.Config.DefaultProfile)
            ?? Environments.Default;
        ProfileNames.Validate(profileName);

        ProfileConfig profile = sources.Config.Profiles.GetValueOrDefault(profileName) ?? new ProfileConfig();

        Uri mcpUrl = ParseUrl(flags.Url, "--url")
            ?? EnvironmentUrl(flags.Environment, "--env")
            ?? ParseUrl(Variable(EnvironmentVariables.McpUrl), EnvironmentVariables.McpUrl)
            ?? EnvironmentUrl(Variable(EnvironmentVariables.Environment), EnvironmentVariables.Environment)
            ?? ParseUrl(project.Url, ProjectConfig.FileName)
            ?? EnvironmentUrl(project.Environment, ProjectConfig.FileName)
            ?? ParseUrl(profile.Url, $"profile '{profileName}'")
            ?? EnvironmentUrl(profile.Environment, $"profile '{profileName}'")
            ?? (Environments.TryGetUrl(profileName, out Uri named) ? named : Environments.McpUrls[Environments.Default]);

        var agent = AgentDetection.Detect(sources.GetVariable);

        return new ResolvedSettings
        {
            ProfileName = profileName,
            EnvironmentName = Environments.NameOf(mcpUrl),
            McpUrl = mcpUrl,
            Connection = FirstOf(flags.Connection, Variable(EnvironmentVariables.Connection), project.Connection, profile.Connection),
            Intent = FirstOf(flags.Intent, Variable(EnvironmentVariables.Intent)),
            Output = ResolveOutput(sources, flags, Variable, agent),
            Agent = agent,
            IsInteractive = !flags.NonInteractive && agent is null && !sources.IsInputRedirected,
            AssumeYes = flags.Yes,
            Timeout = ResolveTimeout(flags.TimeoutSeconds),
            NoWait = flags.NoWait,
            EnvironmentToken = Variable(EnvironmentVariables.Token),
            EnvironmentClientId = Variable(EnvironmentVariables.ClientId),
        };
    }

    public static OutputFormat ParseOutputFormat(string value, string source) =>
        value.Trim().ToLowerInvariant() switch
        {
            "json" => OutputFormat.Json,
            "table" => OutputFormat.Table,
            "csv" => OutputFormat.Csv,
            _ => throw CliException.Usage($"{source} must be json, table or csv, not '{value}'."),
        };

    private static string? ProfileNameFromTarget(CommandLineValues flags, Func<string, string?> variable, ProjectConfig project)
    {
        if (ParseUrl(flags.Url, "--url") is { } flagUrl)
        {
            return Environments.NameOf(flagUrl) ?? ProfileNames.FromUrl(flagUrl);
        }

        if (NullIfBlank(flags.Environment) is { } flagEnvironment)
        {
            return flagEnvironment;
        }

        if (ParseUrl(variable(EnvironmentVariables.McpUrl), EnvironmentVariables.McpUrl) is { } variableUrl)
        {
            return Environments.NameOf(variableUrl) ?? ProfileNames.FromUrl(variableUrl);
        }

        if (variable(EnvironmentVariables.Environment) is { } variableEnvironment)
        {
            return variableEnvironment;
        }

        if (ParseUrl(project.Url, ProjectConfig.FileName) is { } projectUrl)
        {
            return Environments.NameOf(projectUrl) ?? ProfileNames.FromUrl(projectUrl);
        }

        return NullIfBlank(project.Environment);
    }

    private static OutputSettings ResolveOutput(
        SettingsSources sources,
        CommandLineValues flags,
        Func<string, string?> variable,
        string? agent)
    {
        OutputFormat? explicitFormat =
            NullIfBlank(flags.Output) is { } flag ? ParseOutputFormat(flag, "--output")
            : variable(EnvironmentVariables.Output) is { } fromVariable ? ParseOutputFormat(fromVariable, EnvironmentVariables.Output)
            : NullIfBlank(sources.Config.DefaultOutput) is { } fromConfig ? ParseOutputFormat(fromConfig, "defaultOutput in config.json")
            : null;

        OutputFormat format = explicitFormat
            ?? (sources.IsOutputRedirected || agent is not null ? OutputFormat.Json : OutputFormat.Table);

        return new OutputSettings
        {
            Format = format,
            IsFormatExplicit = explicitFormat is not null,
            Quiet = flags.Quiet,
            Compact = flags.Compact,
            UseColor = !flags.NoColor
                && string.IsNullOrEmpty(sources.GetVariable(EnvironmentVariables.NoColor))
                && !sources.IsErrorRedirected,
        };
    }

    private static TimeSpan? ResolveTimeout(int? seconds) => seconds switch
    {
        null => null,
        <= 0 => throw CliException.Usage("--timeout must be a number of seconds greater than 0."),
        _ => TimeSpan.FromSeconds(seconds.Value),
    };

    private static Uri? EnvironmentUrl(string? name, string source)
    {
        if (NullIfBlank(name) is not { } environment)
        {
            return null;
        }

        return Environments.TryGetUrl(environment, out Uri url)
            ? url
            : throw CliException.Usage(
                $"{source} names an unknown environment '{environment}'.",
                $"Use one of {string.Join(", ", Environments.McpUrls.Keys)}, or --url for any other MCP URL.");
    }

    private static Uri? ParseUrl(string? value, string source)
    {
        if (NullIfBlank(value) is not { } text)
        {
            return null;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out Uri? url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
            ? url
            : throw CliException.Usage($"{source} must be an absolute http(s) URL, not '{text}'.");
    }

    private static string? FirstOf(params string?[] values) => values.Select(NullIfBlank).FirstOrDefault(value => value is not null);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
