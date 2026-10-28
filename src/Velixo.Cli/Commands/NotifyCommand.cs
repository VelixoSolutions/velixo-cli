using System.CommandLine;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary><c>velixo notify</c>: <c>send_notification</c>. Declared with the options of the spec, section 8.8; not implemented in this build.</summary>
public static class NotifyCommand
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var subject = new Option<string>("--subject") { Description = "The notification's subject.", HelpName = "text", Required = true };
        var body = new Option<string>("--body") { Description = "The notification's text.", HelpName = "text", Required = true };
        var target = new Option<string?>("--target") { Description = "An email address or a Teams channel link; the signed-in user by default.", HelpName = "email|teams-link" };
        var payload = new Option<string?>("--payload") { Description = "A saved 'query run' output whose result.data is sent as the table.", HelpName = "file" };
        var link = new Option<string?>("--link") { Description = "A link to show with the notification.", HelpName = "url" };
        var linkLabel = new Option<string?>("--link-label") { Description = "The link's label.", HelpName = "text" };
        var severity = new Option<string?>("--severity") { Description = "Info, Warning or Error.", HelpName = "level" };
        severity.AcceptOnlyFromAmong("Info", "Warning", "Error");
        var sourceName = new Option<string?>("--source-name") { Description = "Who sent it; velixo-cli by default.", HelpName = "name" };

        var command = new Command(
            "notify",
            "Send a notification by email or Teams. Example: velixo notify --subject \"AR ready\" --body \"...\" --intent \"...\"")
        {
            subject,
            body,
            target,
            payload,
            link,
            linkLabel,
            severity,
            sourceName,
        };
        command.SetAction(_ => NotImplemented("velixo notify"));
        return command;
    }

    private static int NotImplemented(string path) => throw CliException.NotImplemented(path);
}
