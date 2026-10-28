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

    public int WindowWidth => redirected ? 0 : 120;

    public string StdOut => OutWriter.ToString();

    public string StdErr => ErrorWriter.ToString();
}
