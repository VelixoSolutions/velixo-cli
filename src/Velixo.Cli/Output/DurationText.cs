namespace Velixo.Cli.Output;

/// <summary>Writes a duration the way the progress lines show it: <c>48s</c>, <c>2m14s</c>, <c>1h03m</c>.</summary>
public static class DurationText
{
    public static string Format(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h{duration.Minutes:00}m";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m{duration.Seconds:00}s";
        }

        return $"{(int)duration.TotalSeconds}s";
    }

    public static string FromMilliseconds(long milliseconds) => Format(TimeSpan.FromMilliseconds(milliseconds));
}
