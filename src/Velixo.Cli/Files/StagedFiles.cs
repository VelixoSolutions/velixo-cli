using System.Globalization;
using Velixo.Cli.Config;
using Velixo.Cli.Output;

namespace Velixo.Cli.Files;

/// <summary>
/// The files <c>velixo files stage</c> staged on the profile, by token. The server checks a token
/// together with the name it was issued for, so <c>--attach-token</c> and <c>--file-token</c> look the
/// name up here and the user passes the token alone.
/// </summary>
internal static class StagedFiles
{
    /// <summary>The most files kept: a token lives about an hour, so this is far more than one profile stages at once.</summary>
    private const int MostKept = 50;

    /// <summary>A token whose expiry the server did not give is kept this long.</summary>
    private static readonly TimeSpan _undatedLifetime = TimeSpan.FromDays(1);

    /// <summary>Keeps <paramref name="file"/>, and drops the files whose token has expired, keeping the newest <see cref="MostKept"/>.</summary>
    public static void Remember(CliServices services, StoredStagedFile file)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(file);

        var profile = services.Settings.ProfileName;
        DateTimeOffset now = services.Clock.GetUtcNow();
        file.ExpiresAt ??= now.Add(_undatedLifetime).ToString("O", CultureInfo.InvariantCulture);

        services.Profiles.UpdateStagedFiles(profile, staged =>
        {
            staged.Files.RemoveAll(kept => kept.FileToken == file.FileToken || HasExpired(kept, now));
            staged.Files.Add(file);
            if (staged.Files.Count > MostKept)
            {
                staged.Files.RemoveRange(0, staged.Files.Count - MostKept);
            }
        });
    }

    /// <summary>Returns the files staged on this profile, to look several tokens up against one read.</summary>
    public static IReadOnlyList<StoredStagedFile> Load(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.Profiles.LoadStagedFiles(services.Settings.ProfileName).Files;
    }

    /// <summary>Returns the file staged under <paramref name="token"/> on this profile.</summary>
    /// <exception cref="CliException">Exit 2 when this profile staged no file under that token.</exception>
    public static StoredStagedFile Find(CliServices services, string token) => Find(Load(services), token);

    /// <inheritdoc cref="Find(CliServices, string)"/>
    public static StoredStagedFile Find(IReadOnlyList<StoredStagedFile> staged, string token)
    {
        ArgumentNullException.ThrowIfNull(staged);

        return staged.FirstOrDefault(file => file.FileToken == token) ?? throw Unknown(token);
    }

    private static CliException Unknown(string token) =>
        CliException.Usage(
            $"No file was staged on this profile under the token '{token}'.",
            "Stage the file with 'velixo files stage' on this profile, and pass the token it prints.");

    private static bool HasExpired(StoredStagedFile file, DateTimeOffset now) =>
        !DateTimeOffset.TryParse(file.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset expires)
        || expires <= now;
}
