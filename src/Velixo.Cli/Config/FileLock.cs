namespace Velixo.Cli.Config;

/// <summary>
/// An exclusive lock between the velixo processes on this machine: a lock file held open with
/// <see cref="FileShare.None"/>. On Unix that is <c>flock</c>, which the OS drops when its process dies, so a
/// crashed process never leaves a lock behind.
/// </summary>
internal static class FileLock
{
    private static readonly TimeSpan _poll = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Waits for the lock at <paramref name="path"/> and holds it until disposed. After <paramref name="wait"/>
    /// it goes ahead without the lock: a lock held that long belongs to a process that hangs, and waiting forever
    /// would hang this one too.
    /// </summary>
    public static async Task<IDisposable> AcquireAsync(string path, TimeSpan wait, CancellationToken cancellationToken)
    {
        JsonFile.CreateDirectory(Path.GetDirectoryName(path)!);
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (TryOpen(path) is { } held)
            {
                return held;
            }

            if (waited.Elapsed >= wait)
            {
                return NoLock.Instance;
            }

            await Task.Delay(_poll, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The same lock for a short read-modify-write that runs on no async path.</summary>
    public static IDisposable Acquire(string path, TimeSpan wait)
    {
        JsonFile.CreateDirectory(Path.GetDirectoryName(path)!);
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (TryOpen(path) is { } held)
            {
                return held;
            }

            if (waited.Elapsed >= wait)
            {
                return NoLock.Instance;
            }

            Thread.Sleep(_poll);
        }
    }

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

    private sealed class NoLock : IDisposable
    {
        public static readonly NoLock Instance = new();

        public void Dispose()
        {
        }
    }
}
