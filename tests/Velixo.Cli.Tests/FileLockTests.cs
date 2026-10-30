using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class FileLockTests
{
    [Fact]
    public async Task A_lock_held_longer_than_the_wait_fails_with_file_locked_instead_of_going_ahead()
    {
        using var folder = new TempDirectory();
        var path = Path.Combine(folder.Path, "config.json.lock");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using IDisposable otherProcess = await FileLock.AcquireAsync(path, cancellationToken);

        CliException error = await Assert.ThrowsAsync<CliException>(() =>
            FileLock.AcquireAsync(path, cancellationToken, TimeSpan.FromMilliseconds(200)));

        Assert.Equal(ErrorCodes.FileLocked, error.Code);
        Assert.Contains(path, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wait_for_a_held_lock_ends_when_the_command_is_cancelled()
    {
        using var folder = new TempDirectory();
        var path = Path.Combine(folder.Path, "state.json.lock");
        using IDisposable otherProcess = await FileLock.AcquireAsync(path, TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(() => FileLock.Acquire(path, cancelled.Token), TestContext.Current.CancellationToken));
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void A_lock_file_that_cannot_be_created_fails_at_once_with_file_locked()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes make the folder read-only.");
        using var folder = new TempDirectory();
        var readOnly = Path.Combine(folder.Path, "read-only");
        Directory.CreateDirectory(readOnly);
        File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var waited = System.Diagnostics.Stopwatch.StartNew();
            CliException error = Assert.Throws<CliException>(() => FileLock.Acquire(Path.Combine(readOnly, "config.json.lock"), TestContext.Current.CancellationToken));

            Assert.Equal(ErrorCodes.FileLocked, error.Code);
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(5), "a lock that cannot be created is not waited for");
        }
        finally
        {
            File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
