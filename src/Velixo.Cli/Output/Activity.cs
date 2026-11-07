namespace Velixo.Cli.Output;

/// <summary>
/// Something the CLI waits on, such as the server or a sign-in in the browser, shown on stderr while it lasts.
/// At a terminal it is a spinner whose label redraws in place, as npm shows one; anywhere else it prints each
/// label it is given as a progress line, so a log keeps the same lines as before. Begin one with
/// <see cref="OutputWriter.BeginActivity"/>, and dispose it when the wait ends: that erases the spinner.
/// </summary>
public sealed class Activity : IDisposable
{
    /// <summary>The braille frames npm and most terminal tools spin through.</summary>
    internal static readonly string[] BrailleFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    /// <summary>The frames for a console that cannot show braille, such as an old Windows code page.</summary>
    internal static readonly string[] AsciiFrames = ["|", "/", "-", "\\"];

    /// <summary>How long a wait lasts before the spinner shows, so a quick call never flickers one.</summary>
    internal static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>How long each frame shows.</summary>
    internal static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(80);

    private const string EraseLine = "\r\u001b[2K";
    private const string Cyan = "\u001b[36m";
    private const string Reset = "\u001b[0m";

    private readonly OutputWriter _owner;
    private readonly TextWriter? _terminal;
    private readonly ITimer? _timer;
    private readonly bool _color;
    private readonly string[] _frames;
    private readonly Func<int> _width;
    private string _label;
    private int _frame;
    private bool _shown;
    private bool _drawn;
    private bool _disposed;

    /// <summary>An activity that only prints its labels as progress lines, or nothing when <paramref name="silent"/>.</summary>
    internal Activity(OutputWriter owner, string label, bool silent)
    {
        _owner = owner;
        _label = label;
        IsSilent = silent;
        _frames = AsciiFrames;
        _width = static () => 0;
    }

    /// <summary>A spinner on <paramref name="terminal"/>, drawn from <paramref name="clock"/>'s timer.</summary>
    internal Activity(OutputWriter owner, string label, TextWriter terminal, TimeProvider clock, bool color, string[] frames, Func<int> width)
    {
        _owner = owner;
        _label = label;
        _terminal = terminal;
        _color = color;
        _frames = frames;
        _width = width;
        _timer = clock.CreateTimer(static state => ((Activity)state!).Tick(), this, ShowDelay, FrameInterval);
    }

    /// <summary>Gets whether this activity is a spinner, rather than progress lines.</summary>
    public bool IsAnimated => _terminal is not null;

    /// <summary>Gets whether this activity shows nothing: one nested in another, or begun under <c>--quiet</c> or a full-screen view.</summary>
    internal bool IsSilent { get; }

    /// <summary>Gets the label the activity shows now.</summary>
    public string Label
    {
        get
        {
            lock (_owner.Sync)
            {
                return _label;
            }
        }
    }

    /// <summary>
    /// Changes what the activity says, such as a task's progress after each poll. A spinner redraws its line; anywhere
    /// else the label prints as a progress line.
    /// </summary>
    public void Update(string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (IsSilent)
        {
            return;
        }

        if (!IsAnimated)
        {
            _owner.Progress(label);
            return;
        }

        lock (_owner.Sync)
        {
            _label = label;
            if (_shown && !_disposed)
            {
                DrawLocked();
            }
        }
    }

    public void Dispose()
    {
        lock (_owner.Sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            EraseLocked();
            _owner.End(this);
        }

        _timer?.Dispose();
    }

    /// <summary>Draws the next frame. The timer calls it; a test can too.</summary>
    internal void Tick()
    {
        lock (_owner.Sync)
        {
            if (_disposed || _terminal is null || _owner.ActivitiesHeld)
            {
                return;
            }

            _shown = true;
            DrawLocked();
            _frame = (_frame + 1) % _frames.Length;
        }
    }

    /// <summary>Erases the spinner's line so another line can print in its place; the next frame draws it again below.</summary>
    internal void EraseLocked()
    {
        if (!_drawn || _terminal is null)
        {
            return;
        }

        _terminal.Write(EraseLine);
        _terminal.Flush();
        _drawn = false;
    }

    /// <summary>Draws the spinner again at once, after another line printed in its place.</summary>
    internal void RedrawLocked()
    {
        if (!_disposed && _terminal is not null && _shown)
        {
            DrawLocked();
        }
    }

    private void DrawLocked()
    {
        var frame = _frames[_frame];
        var label = Fit(_label, _width() - 3);
        _terminal!.Write(_color ? $"{EraseLine}{Cyan}{frame}{Reset} {label}" : $"{EraseLine}{frame} {label}");
        _terminal.Flush();
        _drawn = true;
    }

    /// <summary>Cuts a label to the terminal's width, so it never wraps: a wrapped line could not be erased.</summary>
    private static string Fit(string label, int width)
    {
        label = label.ReplaceLineEndings(" ");
        if (width <= 0)
        {
            width = 77;
        }

        return label.Length <= width ? label : string.Concat(label.AsSpan(0, Math.Max(0, width - 1)), "…");
    }
}
