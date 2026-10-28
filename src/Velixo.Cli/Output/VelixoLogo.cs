using System.Globalization;
using System.Text;

namespace Velixo.Cli.Output;

/// <summary>
/// The Velixo mark for a terminal: the symbol as half-block cells beside the name in block letters.
/// With colour on, the symbol carries the brand gradient pixel by pixel and the name runs green to
/// blue from left to right. A terminal too narrow for both gets the name alone, then the plain word.
/// </summary>
public static class VelixoLogo
{
    /// <summary>The columns the name needs; below this the plain word is used.</summary>
    public const int MinimumWidth = 50;

    /// <summary>The columns the symbol and the name need side by side.</summary>
    public const int FullWidth = VelixoSymbol.Columns + Gap + NameWidth;

    private const int Gap = 3;
    private const int NameWidth = 46;
    private const string Reset = "\u001b[0m";

    private static readonly string[] _name =
    [
        "██╗   ██╗███████╗██╗     ██╗██╗  ██╗ ██████╗ ",
        "██║   ██║██╔════╝██║     ██║╚██╗██╔╝██╔═══██╗",
        "██║   ██║█████╗  ██║     ██║ ╚███╔╝ ██║   ██║",
        "╚██╗ ██╔╝██╔══╝  ██║     ██║ ██╔██╗ ██║   ██║",
        " ╚████╔╝ ███████╗███████╗██║██╔╝ ██╗╚██████╔╝",
        "  ╚═══╝  ╚══════╝╚══════╝╚═╝╚═╝  ╚═╝ ╚═════╝ ",
    ];

    /// <summary>The gradient of the symbol's upper bar, green to blue, spread across the name left to right.</summary>
    private static readonly (byte R, byte G, byte B)[] _nameRamp =
    [
        (0x2e, 0xf0, 0xa0),
        (0x52, 0xdc, 0xd0),
        (0x7f, 0xb9, 0xff),
        (0x2b, 0x5c, 0xff),
    ];

    /// <summary>Renders the mark for a terminal <paramref name="width"/> columns wide, with ANSI colour when <paramref name="useColor"/>.</summary>
    public static string Render(bool useColor, int width)
    {
        if (width > 0 && width < MinimumWidth)
        {
            return useColor ? $"{Foreground(_nameRamp[3])}Velixo{Reset}" : "Velixo";
        }

        var withSymbol = width <= 0 || width >= FullWidth;
        var lines = new List<string>();
        var nameOffset = withSymbol ? (VelixoSymbol.Rows - _name.Length) / 2 : 0;
        var rowCount = withSymbol ? VelixoSymbol.Rows : _name.Length;

        for (var row = 0; row < rowCount; row++)
        {
            var line = new StringBuilder();
            if (withSymbol)
            {
                line.Append(RenderSymbolRow(row, useColor));
                line.Append(' ', Gap);
            }

            var nameRow = row - nameOffset;
            if (nameRow >= 0 && nameRow < _name.Length)
            {
                line.Append(useColor ? ColourName(_name[nameRow]) : _name[nameRow]);
            }

            lines.Add(line.ToString().TrimEnd());
        }

        return string.Join('\n', lines);
    }

    private static string RenderSymbolRow(int row, bool useColor)
    {
        var cells = VelixoSymbol.Cells[row];
        var line = new StringBuilder();
        for (var column = 0; column < VelixoSymbol.Columns; column++)
        {
            var top = ParseColour(cells.AsSpan(column * 12, 6));
            var bottom = ParseColour(cells.AsSpan(column * 12 + 6, 6));
            if (useColor)
            {
                line.Append(ColourCell(top, bottom));
            }
            else
            {
                line.Append(top is not null && bottom is not null ? '█' : top is not null ? '▀' : bottom is not null ? '▄' : ' ');
            }
        }

        return line.Append(useColor ? Reset : string.Empty).ToString();
    }

    private static string ColourCell((byte R, byte G, byte B)? top, (byte R, byte G, byte B)? bottom) =>
        (top, bottom) switch
        {
            (null, null) => "\u001b[49m ",
            ({ } t, null) => $"\u001b[49m{Foreground(t)}▀",
            (null, { } b) => $"\u001b[49m{Foreground(b)}▄",
            ({ } t, { } b) => $"{Foreground(t)}{Background(b)}▀",
        };

    private static string ColourName(string row)
    {
        var line = new StringBuilder();
        for (var column = 0; column < row.Length; column++)
        {
            if (column % 4 == 0)
            {
                line.Append(Foreground(Ramp(column / (double)(row.Length - 1))));
            }

            line.Append(row[column]);
        }

        return line.Append(Reset).ToString();
    }

    private static (byte R, byte G, byte B) Ramp(double position)
    {
        var scaled = Math.Clamp(position, 0, 1) * (_nameRamp.Length - 1);
        var index = Math.Min((int)scaled, _nameRamp.Length - 2);
        var fraction = scaled - index;
        var (fromR, fromG, fromB) = _nameRamp[index];
        var (toR, toG, toB) = _nameRamp[index + 1];
        return (Mix(fromR, toR, fraction), Mix(fromG, toG, fraction), Mix(fromB, toB, fraction));
    }

    private static byte Mix(byte from, byte to, double fraction) => (byte)Math.Round(from + (to - from) * fraction);

    private static (byte R, byte G, byte B)? ParseColour(ReadOnlySpan<char> hex) =>
        hex[0] == '-'
            ? null
            : (byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
               byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
               byte.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static string Foreground((byte R, byte G, byte B) c) => $"\u001b[38;2;{c.R};{c.G};{c.B}m";

    private static string Background((byte R, byte G, byte B) c) => $"\u001b[48;2;{c.R};{c.G};{c.B}m";
}
