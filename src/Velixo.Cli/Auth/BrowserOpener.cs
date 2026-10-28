using System.ComponentModel;
using System.Diagnostics;

namespace Velixo.Cli.Auth;

/// <summary>Opens the default browser with the platform's own launcher: <c>open</c>, <c>xdg-open</c> or <c>cmd /c start</c>.</summary>
public sealed class BrowserOpener : IBrowserOpener
{
    public bool TryOpen(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

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
