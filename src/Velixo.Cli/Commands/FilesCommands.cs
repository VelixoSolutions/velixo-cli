using System.CommandLine;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo files upload|request|staged|download</c>: <c>get_upload_url</c>, <c>list_staged_files</c>
/// and the server's download links. Declared with the options of the spec, section 8.6; not
/// implemented in this build.
/// </summary>
public static class FilesCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new Command("files", "Stage files for the ERP and download what it produced.")
        {
            BuildUpload(),
            BuildRequest(),
            BuildStaged(),
            BuildDownload(),
        };
    }

    private static Command BuildUpload()
    {
        var paths = new Argument<string[]>("path") { Description = "The files to stage.", Arity = ArgumentArity.OneOrMore };
        var comment = new Option<string?>("--comment") { Description = "A comment kept with each file.", HelpName = "text" };

        var command = new Command("upload", "Stage files and print a fileToken for each. Example: velixo files upload invoice.pdf") { paths, comment };
        command.SetAction(_ => NotImplemented("velixo files upload"));
        return command;
    }

    private static Command BuildRequest()
    {
        var maxFiles = new Option<int?>("--max-files") { Description = "How many files the page accepts.", HelpName = "n" };

        var command = new Command("request", "Create an upload page for the user to add files to.") { maxFiles };
        command.SetAction(_ => NotImplemented("velixo files request"));
        return command;
    }

    private static Command BuildStaged()
    {
        var batchToken = new Argument<string>("batch-token") { Description = "The uploadBatchToken 'files request' printed." };
        var wait = new Option<bool>("--wait") { Description = "Wait until the user sends the files." };

        var command = new Command("staged", "List the files the user staged on an upload page.") { batchToken, wait };
        command.SetAction(_ => NotImplemented("velixo files staged"));
        return command;
    }

    private static Command BuildDownload()
    {
        var urls = new Argument<string[]>("url") { Description = "Download links the server issued.", Arity = ArgumentArity.ZeroOrMore };
        var output = new Option<string?>("--out") { Description = "Save the one file to this path.", HelpName = "path" };
        var directory = new Option<string?>("--dir") { Description = "Save the files into this directory.", HelpName = "dir" };
        var from = new Option<string?>("--from") { Description = "Download every attachment of a saved 'screen run' output.", HelpName = "result.json" };

        var command = new Command("download", "Download reports, attachments and workbooks by their links.") { urls, output, directory, from };
        command.SetAction(_ => NotImplemented("velixo files download"));
        return command;
    }

    private static int NotImplemented(string path) => throw CliException.NotImplemented(path);
}
