namespace Velixo.Cli;

/// <summary>The process's standard streams and whether each one is a terminal, so tests can replace them.</summary>
public interface ICliConsole
{
    TextWriter Out { get; }

    TextWriter Error { get; }

    TextReader In { get; }

    bool IsOutputRedirected { get; }

    bool IsErrorRedirected { get; }

    bool IsInputRedirected { get; }

    /// <summary>Gets the terminal width in characters, or 0 when stdout is not a terminal.</summary>
    int WindowWidth { get; }

    /// <summary>Gets the terminal height in lines, or 0 when stdout is not a terminal.</summary>
    int WindowHeight { get; }

    /// <summary>Gets whether a key is waiting, so a screen can wait for one and still notice the terminal change size.</summary>
    bool KeyAvailable { get; }

    /// <summary>Reads one key from the terminal without echoing it. Ctrl+C comes back as a key, so a picker can cancel cleanly.</summary>
    ConsoleKeyInfo ReadKey();
}
