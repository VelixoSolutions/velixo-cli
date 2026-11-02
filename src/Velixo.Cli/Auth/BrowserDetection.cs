namespace Velixo.Cli.Auth;

/// <summary>The kind of machine <see cref="BrowserDetection"/> judges.</summary>
public enum BrowserPlatform
{
    Windows,
    MacOS,
    Unix,
}

/// <summary>
/// Tells whether <c>velixo login</c> can open a browser the person at the keyboard will see, so that a server,
/// a container or an SSH session signs in without a browser on its own, as <c>--no-browser</c> asks.
/// </summary>
public static class BrowserDetection
{
    /// <summary>
    /// Returns why no browser can be opened here, or null when one can. <c>BROWSER</c> names a browser the person
    /// chose, such as the one a remote editor forwards to their own machine, so it always counts as one. Over SSH
    /// a browser opens on the remote machine's screen, which the person does not see, unless X11 forwarding gives
    /// the session a display. On Linux and the other Unix systems, a browser needs a graphical display and
    /// <c>xdg-open</c> to start it.
    /// </summary>
    /// <param name="getVariable">Reads an environment variable.</param>
    /// <param name="platform">The machine's operating system.</param>
    /// <param name="isOnPath">Tells whether a program of that name is on the <c>PATH</c>.</param>
    public static string? UnavailableReason(Func<string, string?> getVariable, BrowserPlatform platform, Func<string, bool> isOnPath)
    {
        ArgumentNullException.ThrowIfNull(getVariable);
        ArgumentNullException.ThrowIfNull(isOnPath);

        bool Set(string name) => !string.IsNullOrWhiteSpace(getVariable(name));

        if (Set("BROWSER"))
        {
            return null;
        }

        var hasDisplay = Set("DISPLAY") || Set("WAYLAND_DISPLAY");
        var overSsh = Set("SSH_CONNECTION") || Set("SSH_CLIENT") || Set("SSH_TTY");

        if (overSsh && (platform != BrowserPlatform.Unix || !hasDisplay))
        {
            return "this is an SSH session";
        }

        if (platform != BrowserPlatform.Unix)
        {
            return null;
        }

        if (!hasDisplay)
        {
            return "this machine has no graphical display";
        }

        return isOnPath("xdg-open") ? null : "xdg-open is not installed";
    }

    /// <summary>Tells whether an executable named <paramref name="program"/> is in a directory of the <c>PATH</c>.</summary>
    public static bool IsOnPath(string program, string? path)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(directory, program)))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // A PATH entry with characters no path can hold names no program.
            }
        }

        return false;
    }
}
