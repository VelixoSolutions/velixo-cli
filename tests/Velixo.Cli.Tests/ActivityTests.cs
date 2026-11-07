using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class ActivityTests
{
    private const string Erase = "\r\u001b[2K";

    private readonly TestConsole _console = new(redirected: false);

    [Fact]
    public void A_spinner_shows_nothing_until_its_first_frame()
    {
        OutputWriter output = Animated();

        using Activity activity = output.BeginActivity("Waiting for Velixo", announce: true);

        Assert.True(activity.IsAnimated);
        Assert.Equal(string.Empty, _console.StdErr);
    }

    [Fact]
    public void A_spinner_draws_each_frame_in_place_and_erases_itself_when_done()
    {
        OutputWriter output = Animated();

        Activity activity = output.BeginActivity("Waiting for Velixo");
        activity.Tick();
        activity.Tick();
        activity.Dispose();

        Assert.Equal($"{Erase}⠋ Waiting for Velixo{Erase}⠙ Waiting for Velixo{Erase}", _console.StdErr);
    }

    [Fact]
    public void A_new_label_redraws_the_spinner_line()
    {
        OutputWriter output = Animated();

        using Activity activity = output.BeginActivity("Waiting for Velixo");
        activity.Tick();
        activity.Update("Release on SO301000, 12s");

        Assert.EndsWith($"{Erase}⠙ Release on SO301000, 12s", _console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void A_progress_line_prints_above_the_spinner_which_draws_again_below_it()
    {
        OutputWriter output = Animated();

        using Activity activity = output.BeginActivity("Waiting for Velixo");
        activity.Tick();
        output.Progress("staging a.csv");

        Assert.EndsWith($"{Erase}staging a.csv\n{Erase}⠙ Waiting for Velixo", _console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nested_activity_leaves_the_outer_label_alone()
    {
        OutputWriter output = Animated();

        using Activity outer = output.BeginActivity("Waiting for the task");
        using (Activity inner = output.BeginActivity("Waiting for Velixo"))
        {
            Assert.False(inner.IsAnimated);
            inner.Update("something else");
        }

        outer.Tick();

        Assert.Equal($"{Erase}⠋ Waiting for the task", _console.StdErr);
    }

    [Fact]
    public void A_long_label_is_cut_to_the_terminal_width()
    {
        _console.Width = 20;
        OutputWriter output = Animated();

        using Activity activity = output.BeginActivity("Waiting for a task with a very long label");
        activity.Tick();

        Assert.Equal($"{Erase}⠋ Waiting for a ta…", _console.StdErr);
    }

    [Fact]
    public void A_held_screen_gets_no_spinner()
    {
        OutputWriter output = Animated();

        using (output.HoldActivities())
        {
            using Activity activity = output.BeginActivity("Waiting for Velixo");
            activity.Tick();
            Assert.False(activity.IsAnimated);
        }

        Assert.Equal(string.Empty, _console.StdErr);
    }

    [Fact]
    public void Without_a_terminal_each_label_is_a_progress_line()
    {
        var output = new OutputWriter(_console, new OutputSettings());

        using Activity activity = output.BeginActivity("Waiting for the sign-in...", announce: true);
        activity.Update("Release on SO301000, 12s");

        Assert.False(activity.IsAnimated);
        Assert.Equal("Waiting for the sign-in...\nRelease on SO301000, 12s\n", _console.StdErr);
    }

    [Fact]
    public void Quiet_shows_no_activity()
    {
        var output = new OutputWriter(_console, new OutputSettings { Quiet = true });

        using Activity activity = output.BeginActivity("Waiting for the sign-in...", announce: true);
        activity.Update("Release on SO301000, 12s");

        Assert.Equal(string.Empty, _console.StdErr);
    }

    private OutputWriter Animated() =>
        new(_console, new OutputSettings { Animate = true }, new StoppedTimers())
        {
            SpinnerFrames = Activity.BrailleFrames,
        };

    /// <summary>A clock whose timers never fire, so each test draws the frames itself with <see cref="Activity.Tick"/>.</summary>
    private sealed class StoppedTimers : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Stopped();

        private sealed class Stopped : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
