namespace Velixo.Cli.Auth;

public interface IBrowserOpener
{
    /// <summary>Opens <paramref name="url"/> in the default browser; false when that could not be started.</summary>
    bool TryOpen(Uri url);
}
