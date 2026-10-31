namespace Velixo.Cli.Tests.Fakes;

internal sealed class TestConsole(bool redirected = true, string input = "") : ICliConsole
{
    public StringWriter OutWriter { get; } = new() { NewLine = "\n" };

    public StringWriter ErrorWriter { get; } = new() { NewLine = "\n" };

    public TextWriter Out => OutWriter;

    public TextWriter Error => ErrorWriter;

    public TextReader In { get; } = new StringReader(input);

    public bool IsOutputRedirected => redirected;

    public bool IsErrorRedirected => redirected;

    public bool IsInputRedirected => redirected;

    /// <summary>Gets or sets the terminal width a test runs in; 0 when stdout is redirected.</summary>
    public int Width { get; set; } = 120;

    public int WindowWidth => redirected ? 0 : Width;

    /// <summary>Gets or sets the terminal height a test runs in; 0 when stdout is redirected.</summary>
    public int Height { get; set; } = 30;

    public int WindowHeight => redirected ? 0 : Height;

    /// <summary>Gets the keys <see cref="ReadKey"/> hands out, in order.</summary>
    public Queue<ConsoleKeyInfo> Keys { get; } = new();

    /// <summary>Gets the sizes the terminal takes, in order, each while a screen waits for its next key.</summary>
    public Queue<(int Width, int Height)> Resizes { get; } = new();

    /// <summary>Gets whether a key is waiting: false once for each resize, which it applies, and true otherwise.</summary>
    public bool KeyAvailable
    {
        get
        {
            if (!Resizes.TryDequeue(out (int Width, int Height) size))
            {
                return true;
            }

            (Width, Height) = size;
            return false;
        }
    }

    public ConsoleKeyInfo ReadKey() =>
        Keys.TryDequeue(out ConsoleKeyInfo key) ? key : throw new InvalidOperationException("The test pressed no more keys.");

    public string StdOut => OutWriter.ToString();

    public string StdErr => ErrorWriter.ToString();
}
