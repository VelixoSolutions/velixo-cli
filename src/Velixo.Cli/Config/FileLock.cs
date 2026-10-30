namespace Velixo.Cli.Config;

/// <summary>
/// An exclusive lock between the velixo processes on this machine: a lock file held open with
/// <see cref="FileShare.None"/>. On Unix that is <c>flock</c>, which the OS drops when its process dies, so a
/// lock that is held belongs to a live process, and a crashed process never leaves one behind.
/// </summary>
internal static class FileLock
{
    private static readonly TimeSpan _poll = TimeSpan.FromMilliseconds(25);

    /// <summary>Waits until the lock at <paramref name="path"/> is free, or the command is cancelled, and holds it until disposed.</summary>
    public static async Task<IDisposable> AcquireAsync(string path, CancellationToken cancellationToken)
    {
        JsonFile.CreateDirectory(Path.GetDirectoryName(path)!);
        while (true)
        {
            if (TryOpen(path) is { } held)
            {
                return held;
            }

            await Task.Delay(_poll, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The same lock, for a short read-modify-write of a file on a path that is not async.</summary>
    public static IDisposable Acquire(string path) => AcquireAsync(path, CancellationToken.None).GetAwaiter().GetResult();

    private static FileStream? TryOpen(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception) when (exception is not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException))
        {
            return null;
        }
    }
}
