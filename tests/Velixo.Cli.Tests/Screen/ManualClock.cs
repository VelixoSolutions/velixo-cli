namespace Velixo.Cli.Tests.Screen;

/// <summary>A clock that stands still until the test moves it.</summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => Now;

    public override long GetTimestamp() => Now.UtcTicks;

    public void Advance(TimeSpan by) => Now += by;
}
