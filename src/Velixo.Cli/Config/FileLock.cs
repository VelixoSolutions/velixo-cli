using Velixo.Cli.Output;

namespace Velixo.Cli.Config;

/// <summary>
/// An exclusive lock between the velixo processes on this machine: a lock file held open with
/// <see cref="FileShare.None"/>. On Unix that is <c>flock</c>, which the OS drops when its process dies, so a
/// lock that is held belongs to a live process, and a crashed process never leaves one behind.
/// </summary>
internal static class FileLock
{
    /// <summary>How long a short read-modify-write of one of the CLI's files waits for another process to finish its own.</summary>
    internal static readonly TimeSpan UpdateWait = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan _poll = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Waits until the lock at <paramref name="path"/> is free and holds it until disposed. It waits until
    /// <paramref name="cancellationToken"/> is cancelled, or, when <paramref name="wait"/> is given, fails with
    /// <see cref="ErrorCodes.FileLocked"/> after that long.
    /// </summary>
    public static async Task<IDisposable> AcquireAsync(string path, CancellationToken cancellationToken, TimeSpan? wait = null)
    {
        JsonFile.CreateDirectory(Path.GetDirectoryName(path)!);
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (TryOpen(path) is { } held)
            {
                return held;
            }

            if (wait is { } limit && waited.Elapsed >= limit)
            {
                throw new CliException(
                    ExitCodes.Failure,
                    ErrorCodes.FileLocked,
                    $"Another velixo process has held {path} for more than {limit.TotalSeconds:0} seconds.",
                    "Wait for the other velixo command to finish, or stop it, then run this command again.");
            }

            await Task.Delay(_poll, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The same lock for a short read-modify-write on a path that is not async: it fails after <see cref="UpdateWait"/>.</summary>
    public static IDisposable Acquire(string path, CancellationToken cancellationToken = default) =>
        AcquireAsync(path, cancellationToken, UpdateWait).GetAwaiter().GetResult();

    private static FileStream? TryOpen(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception) when (IsHeldByAnotherProcess(exception))
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.FileLocked,
                $"The lock file {path} cannot be opened: {exception.Message}",
                "Check that the configuration folder is writable and on a file system that supports file locks, or move it with VELIXO_CONFIG_DIR.");
        }
    }

    /// <summary>
    /// Whether opening the lock failed only because another process holds it: a sharing or lock violation
    /// on Windows, and on Unix the EWOULDBLOCK that a refused <c>flock</c> reports (11 on Linux, 35 on macOS).
    /// </summary>
    internal static bool IsHeldByAnotherProcess(IOException exception) =>
        OperatingSystem.IsWindows()
            ? (exception.HResult & 0xFFFF) is 32 or 33
            : exception.HResult is 11 or 35;
}
