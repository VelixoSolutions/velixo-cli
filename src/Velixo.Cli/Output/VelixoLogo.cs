namespace Velixo.Cli.Output;

/// <summary>
/// The Velixo word mark as block letters for a terminal, coloured as a top-to-bottom ramp of the
/// brand blues when colour is on. Narrow terminals get the plain name instead of broken art.
/// </summary>
public static class VelixoLogo
{
    /// <summary>The columns the block letters need; below this the plain name is used.</summary>
    public const int MinimumWidth = 50;

    private static readonly string[] _rows =
    [
        "██╗   ██╗███████╗██╗     ██╗██╗  ██╗ ██████╗ ",
        "██║   ██║██╔════╝██║     ██║╚██╗██╔╝██╔═══██╗",
        "██║   ██║█████╗  ██║     ██║ ╚███╔╝ ██║   ██║",
        "╚██╗ ██╔╝██╔══╝  ██║     ██║ ██╔██╗ ██║   ██║",
        " ╚████╔╝ ███████╗███████╗██║██╔╝ ██╗╚██████╔╝",
        "  ╚═══╝  ╚══════╝╚══════╝╚═╝╚═╝  ╚═╝ ╚═════╝ ",
    ];

    /// <summary>One brand blue per row, light to dark: the gradient of the Velixo symbol, read downwards.</summary>
    private static readonly (byte R, byte G, byte B)[] _ramp =
    [
        (0x7f, 0x9a, 0xf5),
        (0x5f, 0x80, 0xee),
        (0x45, 0x6a, 0xe3),
        (0x34, 0x5a, 0xd9),
        (0x2f, 0x4d, 0xbd),
        (0x2e, 0x42, 0x9b),
    ];

    /// <summary>Renders the mark for a terminal <paramref name="width"/> columns wide, with ANSI colour when <paramref name="useColor"/>.</summary>
    public static string Render(bool useColor, int width)
    {
        if (width > 0 && width < MinimumWidth)
        {
            return useColor ? $"{Ansi(_ramp[3])}Velixo{Reset}" : "Velixo";
        }

        var lines = new List<string>(_rows.Length);
        for (var index = 0; index < _rows.Length; index++)
        {
            lines.Add(useColor ? $"{Ansi(_ramp[index])}{_rows[index]}{Reset}" : _rows[index]);
        }

        return string.Join('\n', lines);
    }

    private const string Reset = "\u001b[0m";

    private static string Ansi((byte R, byte G, byte B) colour) => $"\u001b[38;2;{colour.R};{colour.G};{colour.B}m";
}
