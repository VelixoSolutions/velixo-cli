namespace Velixo.Cli.Auth;

public interface IBrowserOpener
{
    /// <summary>
    /// Gets why no browser the person would see can be opened here, such as an SSH session or a machine without
    /// a display, or null when <see cref="TryOpen"/> is worth trying. <c>velixo login</c> then signs in as
    /// <c>--no-browser</c> does.
    /// </summary>
    string? UnavailableReason => null;

    /// <summary>Opens <paramref name="url"/> in the default browser; false when that could not be started.</summary>
    bool TryOpen(Uri url);
}
