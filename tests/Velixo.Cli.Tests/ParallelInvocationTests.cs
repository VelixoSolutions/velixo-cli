using Velixo.Cli.Auth;
using Velixo.Cli.Config;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

/// <summary>
/// Runs the stores' updates at the same time from separate store objects, as separate velixo processes do,
/// and checks that no update is lost.
/// </summary>
public sealed class ParallelInvocationTests
{
    private const int Invocations = 24;

    [Fact]
    public async Task Builds_saved_at_the_same_time_each_keep_their_own_library_number()
    {
        using var folder = new TempDirectory();
        var paths = new ConfigPaths(folder.Path);

        await Parallel.ForEachAsync(Enumerable.Range(0, Invocations), TestContext.Current.CancellationToken, (index, _) =>
        {
            new ProfileStore(paths).UpdateLibrary("prod", library =>
                library.Add(LibraryKind.Query, new LibraryEntry { Title = $"query {index}" }));
            return ValueTask.CompletedTask;
        });

        List<LibraryEntry> queries = new ProfileStore(paths).LoadLibrary("prod").Queries;
        Assert.Equal(Invocations, queries.Count);
        Assert.Equal(Enumerable.Range(1, Invocations), queries.Select(entry => entry.Number).Order());
    }

    [Fact]
    public async Task State_changes_made_at_the_same_time_are_all_kept()
    {
        using var folder = new TempDirectory();
        var paths = new ConfigPaths(folder.Path);

        await Parallel.ForEachAsync(Enumerable.Range(0, Invocations), TestContext.Current.CancellationToken, (index, _) =>
        {
            new ProfileStore(paths).UpdateState("prod", state =>
                state.RecentWrites.Add(new RecentWrite { Fingerprint = $"write {index}", ExecutedAt = DateTimeOffset.UtcNow }));
            return ValueTask.CompletedTask;
        });

        Assert.Equal(Invocations, new ProfileStore(paths).LoadState("prod").RecentWrites.Count);
    }

    [Fact]
    public async Task Profiles_configured_at_the_same_time_are_all_kept()
    {
        using var folder = new TempDirectory();
        var paths = new ConfigPaths(folder.Path);

        await Parallel.ForEachAsync(Enumerable.Range(0, Invocations), TestContext.Current.CancellationToken, (index, _) =>
        {
            new ProfileStore(paths).UpdateConfig(config => config.Profiles[$"profile-{index}"] = new ProfileConfig { Connection = $"connection-{index}" });
            return ValueTask.CompletedTask;
        });

        Assert.Equal(Invocations, new ProfileStore(paths).LoadConfig().Profiles.Count);
    }

    [Fact]
    public async Task Sign_ins_saved_at_the_same_time_for_different_profiles_are_all_kept()
    {
        using var folder = new TempDirectory();
        var paths = new ConfigPaths(folder.Path);

        await Parallel.ForEachAsync(Enumerable.Range(0, Invocations), TestContext.Current.CancellationToken, (index, _) =>
        {
            new TokenStore(paths).Save($"profile-{index}", new StoredCredentials { RefreshToken = $"refresh-{index}" });
            return ValueTask.CompletedTask;
        });

        Assert.Equal(Invocations, new TokenStore(paths).ListProfiles().Count);
    }
}
