using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Serialization;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo config</c>: reads and writes <c>config.json</c>. The keys are <c>defaultProfile</c>,
/// <c>defaultOutput</c>, and <c>profiles.&lt;name&gt;.environment|url|connection</c>.
/// </summary>
public static class ConfigCommands
{
    private const string ProfilesPrefix = "profiles.";

    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var getKey = new Argument<string>("key") { Description = "defaultProfile, defaultOutput, or profiles.<name>.environment|url|connection." };
        var get = new Command("get", "Print one setting. Example: velixo config get defaultProfile") { getKey };
        get.SetAction(parse =>
        {
            var key = parse.GetValue(getKey)!;
            services.Output.WriteResult(new JsonObject { ["key"] = key, ["value"] = Read(services.Profiles.LoadConfig(), key) });
            return ExitCodes.Success;
        });

        var setKey = new Argument<string>("key") { Description = "defaultProfile, defaultOutput, or profiles.<name>.environment|url|connection." };
        var setValue = new Argument<string>("value") { Description = "The new value; an empty string removes the setting." };
        var set = new Command("set", "Change one setting. Example: velixo config set defaultOutput json") { setKey, setValue };
        set.SetAction(parse =>
        {
            var key = parse.GetValue(setKey)!;
            var value = parse.GetValue(setValue);
            CliConfig config = services.Profiles.LoadConfig();
            Write(config, key, string.IsNullOrEmpty(value) ? null : value);
            services.Profiles.SaveConfig(config);
            services.Output.WriteResult(new JsonObject { ["key"] = key, ["value"] = Read(config, key) });
            return ExitCodes.Success;
        });

        var list = new Command("list", "Print the whole configuration.");
        list.SetAction(_ =>
        {
            services.Output.WriteResult(JsonSerializer.SerializeToNode(services.Profiles.LoadConfig(), CliJsonContext.Default.CliConfig));
            return ExitCodes.Success;
        });

        var path = new Command("path", "Print where the configuration, the credentials, the state and the built lists are kept.");
        path.SetAction(_ =>
        {
            ConfigPaths paths = services.Profiles.Paths;
            services.Output.WriteResult(new JsonObject
            {
                ["root"] = paths.Root,
                ["config"] = paths.ConfigFile,
                ["credentials"] = paths.CredentialsFile,
                ["state"] = paths.StateDirectory,
            });
            return ExitCodes.Success;
        });

        return new Command("config", "Read and write the CLI's configuration.") { get, set, list, path };
    }

    public static string? Read(CliConfig config, string key)
    {
        ArgumentNullException.ThrowIfNull(config);

        return key switch
        {
            "defaultProfile" => config.DefaultProfile,
            "defaultOutput" => config.DefaultOutput,
            _ when ParseProfileKey(key) is var (profile, field) => config.Profiles.GetValueOrDefault(profile) is { } settings
                ? field switch
                {
                    "environment" => settings.Environment,
                    "url" => settings.Url,
                    _ => settings.Connection,
                }
                : null,
            _ => throw UnknownKey(key),
        };
    }

    public static void Write(CliConfig config, string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(config);

        switch (key)
        {
            case "defaultProfile":
                config.DefaultProfile = value is null ? null : ProfileNames.Validate(value);
                return;

            case "defaultOutput":
                config.DefaultOutput = value is null ? null : SettingsResolver.ParseOutputFormat(value, key).ToString().ToLowerInvariant();
                return;
        }

        if (ParseProfileKey(key) is not var (profile, field))
        {
            throw UnknownKey(key);
        }

        ProfileConfig settings = config.Profiles.GetValueOrDefault(profile) ?? new ProfileConfig();
        switch (field)
        {
            case "environment":
                if (value is not null && !Environments.McpUrls.ContainsKey(value))
                {
                    throw CliException.Usage($"'{value}' is not a built-in environment.", $"Use one of {string.Join(", ", Environments.McpUrls.Keys)}.");
                }

                settings.Environment = value;
                break;

            case "url":
                if (value is not null && !Uri.TryCreate(value, UriKind.Absolute, out _))
                {
                    throw CliException.Usage($"'{value}' is not an absolute URL.");
                }

                settings.Url = value;
                break;

            default:
                settings.Connection = value;
                break;
        }

        config.Profiles[profile] = settings;
    }

    private static (string Profile, string Field)? ParseProfileKey(string key)
    {
        if (!key.StartsWith(ProfilesPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = key[ProfilesPrefix.Length..];
        var dot = rest.LastIndexOf('.');
        if (dot <= 0)
        {
            return null;
        }

        var profile = rest[..dot];
        var field = rest[(dot + 1)..];
        return ProfileNames.IsValid(profile) && field is "environment" or "url" or "connection" ? (profile, field) : null;
    }

    private static CliException UnknownKey(string key) =>
        CliException.Usage(
            $"'{key}' is not a configuration key.",
            "Use defaultProfile, defaultOutput, or profiles.<name>.environment|url|connection.");
}
