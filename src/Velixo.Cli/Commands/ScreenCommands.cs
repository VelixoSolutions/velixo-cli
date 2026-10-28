using System.CommandLine;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo screen build|run</c>: <c>prepare_commands</c> and <c>execute_commands</c>. Declared with
/// the options of the spec, sections 8.4 and 9; not implemented in this build.
/// </summary>
public static class ScreenCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new Command("screen", "Build and run batches of ERP screen commands.")
        {
            BuildBuild(),
            BuildRun(),
        };
    }

    private static Command BuildBuild()
    {
        var request = new Argument<string>("request")
        {
            Description = "The operation in the user's words, every value as a {{Placeholder}}.",
        };
        var target = new Option<string?>("--target") { Description = "The screen, when the user named it.", HelpName = "screen" };

        var command = new Command(
            "build",
            "Build a reusable batch with prepare_commands. Example: velixo screen build \"Create a sales order for {{CustomerID}}\" --intent \"...\" > order.batch.json")
        {
            request,
            target,
        };
        command.SetAction(_ => NotImplemented("velixo screen build"));
        return command;
    }

    private static Command BuildRun()
    {
        var batch = new Option<string>("--batch") { Description = "The batch file 'screen build' wrote.", HelpName = "file", Required = true };
        var variable = new Option<string[]>("--var")
        {
            Description = "Bind a placeholder: NAME=VALUE. An empty value writes a blank. Repeatable.",
            HelpName = "name=value",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var variableNull = new Option<string[]>("--var-null")
        {
            Description = "Pass an explicit null, so the ERP defaults the field. Repeatable.",
            HelpName = "name",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var variables = new Option<string?>("--vars") { Description = "The variables as a JSON object in a file.", HelpName = "file" };
        var rows = new Option<string?>("--rows") { Description = "The detail rows as a JSON array in a file.", HelpName = "file" };
        var attach = new Option<string[]>("--attach")
        {
            Description = "Stage a file and attach it to the record: PATH or PATH:COMMENT. Repeatable.",
            HelpName = "path[:comment]",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var leaveOpen = new Option<bool>("--leave-open") { Description = "Leave the record open for a later --continue." };
        var continueOpen = new Option<bool>("--continue") { Description = "Continue the record the last --leave-open left open." };
        var continuation = new Option<string?>("--continuation") { Description = "Continue the record a saved continuation names.", HelpName = "file" };
        var finish = new Option<bool>("--finish") { Description = "Run the batch's finish commands on the open record." };
        var downloadDir = new Option<string?>("--download-dir") { Description = "Save every report and attachment the batch produced here.", HelpName = "dir" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Send nothing: bind the variables, report what is missing, print the summary." };
        var force = new Option<bool>("--force") { Description = "Run a batch the duplicate guard would refuse." };

        var command = new Command(
            "run",
            "Run a batch with execute_commands. Writes need --yes or a terminal confirmation. Example: velixo screen run --batch order.batch.json --var CustomerID=ACME --yes --intent \"...\"")
        {
            batch,
            variable,
            variableNull,
            variables,
            rows,
            attach,
            leaveOpen,
            continueOpen,
            continuation,
            finish,
            downloadDir,
            dryRun,
            force,
        };
        command.SetAction(_ => NotImplemented("velixo screen run"));
        return command;
    }

    private static int NotImplemented(string path) => throw CliException.NotImplemented(path);
}
