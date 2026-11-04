using System.ComponentModel;
using System.Diagnostics;
using Velixo.Cli.Config;

namespace Velixo.Cli.Auth;

/// <summary>Opens the browser <c>BROWSER</c> names, or else the default one with the platform's own launcher: <c>open</c>, <c>xdg-open</c> or <c>cmd /c start</c>.</summary>
/// <param name="environment">The environment that names <c>BROWSER</c> and that <see cref="UnavailableReason"/> reads, or null to always try the default browser.</param>
public sealed class BrowserOpener(IEnvironmentReader? environment = null) : IBrowserOpener
{
    private readonly Lazy<string?> _unavailableReason = new(() => environment is null
        ? null
        : BrowserDetection.UnavailableReason(
            environment.GetVariable,
            OperatingSystem.IsWindows() ? BrowserPlatform.Windows : OperatingSystem.IsMacOS() ? BrowserPlatform.MacOS : BrowserPlatform.Unix,
            program => BrowserDetection.IsOnPath(program, environment.GetVariable("PATH"))));

    public string? UnavailableReason => _unavailableReason.Value;

    public bool TryOpen(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        // BROWSER is what BrowserDetection trusts, so it is also what opens the page: a remote editor sets it to a
        // helper that opens the URL on the person's own machine, where xdg-open would open nothing they see.
        foreach (ProcessStartInfo command in ChosenBrowserCommands(environment?.GetVariable("BROWSER"), url))
        {
            if (Start(command))
            {
                return true;
            }
        }

        return Start(PlatformCommand(url));
    }

    /// <summary>
    /// Builds the commands a <c>BROWSER</c> value names: a list separated by the path separator, each a program and
    /// its arguments, where <c>%s</c> stands for the URL and the URL is the last argument when no <c>%s</c> appears.
    /// </summary>
    public static IEnumerable<ProcessStartInfo> ChosenBrowserCommands(string? browser, Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var entry in (browser ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var words = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var start = new ProcessStartInfo { FileName = words[0], UseShellExecute = false, CreateNoWindow = true };
            var placed = false;
            foreach (var word in words.Skip(1))
            {
                placed |= word.Contains("%s", StringComparison.Ordinal);
                start.ArgumentList.Add(word.Replace("%s", url.AbsoluteUri, StringComparison.Ordinal));
            }

            if (!placed)
            {
                start.ArgumentList.Add(url.AbsoluteUri);
            }

            yield return start;
        }
    }

    private static ProcessStartInfo PlatformCommand(Uri url)
    {
        var start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
        if (OperatingSystem.IsWindows())
        {
            start.FileName = "cmd";
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("start");
            start.ArgumentList.Add(string.Empty);
            start.ArgumentList.Add(url.AbsoluteUri.Replace("&", "^&", StringComparison.Ordinal));
        }
        else
        {
            start.FileName = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            start.ArgumentList.Add(url.AbsoluteUri);
        }

        return start;
    }

    private static bool Start(ProcessStartInfo start)
    {
        try
        {
            using Process? process = Process.Start(start);
            return process is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
