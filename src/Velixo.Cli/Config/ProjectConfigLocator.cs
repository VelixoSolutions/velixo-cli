using System.Text.Json;
using Velixo.Cli.Output;
using Velixo.Cli.Serialization;

namespace Velixo.Cli.Config;

/// <summary>Finds <c>.velixo.json</c> in the working directory or the nearest parent that has one.</summary>
public static class ProjectConfigLocator
{
    public static ProjectConfig? Find(string startDirectory)
    {
        for (DirectoryInfo? directory = new(startDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ProjectConfig.FileName);
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                return JsonFile.Read(candidate, CliJsonContext.Default.ProjectConfig) ?? new ProjectConfig();
            }
            catch (JsonException exception)
            {
                throw new CliException(
                    ExitCodes.Usage,
                    ErrorCodes.ConfigInvalid,
                    $"{candidate} is not valid JSON: {exception.Message}",
                    "Fix the file, or delete it to use no project defaults.");
            }
        }

        return null;
    }
}
