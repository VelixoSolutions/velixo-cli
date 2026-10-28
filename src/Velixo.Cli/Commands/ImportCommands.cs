using System.CommandLine;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo import scenarios|run</c>: <c>list_import_scenarios</c> and <c>execute_import_scenario</c>
/// on Acumatica. Declared with the options of the spec, section 8.7; not implemented in this build.
/// </summary>
public static class ImportCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new Command("import", "Run Acumatica import scenarios. --connection is required.")
        {
            BuildScenarios(),
            BuildRun(),
        };
    }

    private static Command BuildScenarios()
    {
        var screen = new Option<string?>("--screen") { Description = "Only the scenarios of this screen.", HelpName = "screen-id" };
        var filter = new Option<string?>("--filter") { Description = "Only the scenarios whose name matches.", HelpName = "pattern" };

        var command = new Command("scenarios", "List the import scenarios and their columns. Example: velixo import scenarios --connection gab-dev") { screen, filter };
        command.SetAction(_ => NotImplemented("velixo import scenarios"));
        return command;
    }

    private static Command BuildRun()
    {
        var scenario = new Option<string>("--scenario") { Description = "The import scenario's name.", HelpName = "name", Required = true };
        var file = new Option<string>("--file") { Description = "The file to import.", HelpName = "path", Required = true };
        var sheet = new Option<string?>("--sheet") { Description = "The worksheet of an Excel file.", HelpName = "name" };
        var range = new Option<string?>("--range") { Description = "The cells to import, such as A1:F200.", HelpName = "range" };
        var validateOnly = new Option<bool>("--validate-only") { Description = "Stop after the validation preview." };
        var breakOnError = new Option<bool>("--break-on-error") { Description = "Stop at the first row that fails." };

        var command = new Command(
            "run",
            "Validate, confirm and run an import. Example: velixo import run --connection gab-dev --scenario \"Import Customers\" --file customers.csv --yes --intent \"...\"")
        {
            scenario,
            file,
            sheet,
            range,
            validateOnly,
            breakOnError,
        };
        command.SetAction(_ => NotImplemented("velixo import run"));
        return command;
    }

    private static int NotImplemented(string path) => throw CliException.NotImplemented(path);
}
