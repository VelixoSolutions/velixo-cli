namespace Velixo.Cli.Commands;

/// <summary>
/// A list a person picks one row from on a terminal: the arrow keys (or j and k) move, Enter picks, and
/// Esc, q or Ctrl+C cancel. It draws on stderr, so stdout keeps only the command's result, and it erases
/// itself when it ends. A long list scrolls inside a window of <see cref="VisibleRows"/> rows.
/// </summary>
internal static class Picker
{
    public const int VisibleRows = 10;

    private const string Hint = "↑/↓ to move, enter to select, esc to cancel";

    /// <summary>Returns the index of the row picked, or null when the person cancelled.</summary>
    public static int? Choose(ICliConsole console, string title, IReadOnlyList<string> rows, int initial, bool useColor)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            return null;
        }

        TextWriter screen = console.Error;
        var selected = Math.Clamp(initial, 0, rows.Count - 1);
        var top = 0;
        var drawn = 0;

        screen.Write("\u001b[?25l");
        try
        {
            while (true)
            {
                top = Math.Clamp(top, Math.Max(0, selected - VisibleRows + 1), selected);
                drawn = Draw(screen, title, rows, selected, top, drawn, useColor);

                ConsoleKeyInfo key = console.ReadKey();
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow or ConsoleKey.K:
                        selected = selected == 0 ? rows.Count - 1 : selected - 1;
                        break;
                    case ConsoleKey.DownArrow or ConsoleKey.J or ConsoleKey.Tab:
                        selected = selected == rows.Count - 1 ? 0 : selected + 1;
                        break;
                    case ConsoleKey.Home:
                        selected = 0;
                        break;
                    case ConsoleKey.End:
                        selected = rows.Count - 1;
                        break;
                    case ConsoleKey.Enter:
                        return selected;
                    case ConsoleKey.Escape or ConsoleKey.Q:
                        return null;
                    case ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                        return null;
                }
            }
        }
        finally
        {
            Erase(screen, drawn);
            screen.Write("\u001b[?25h");
            screen.Flush();
        }
    }

    /// <summary>Draws the title, the window of rows and the hint over the <paramref name="previous"/> lines, and returns how many lines it drew.</summary>
    private static int Draw(TextWriter screen, string title, IReadOnlyList<string> rows, int selected, int top, int previous, bool useColor)
    {
        if (previous > 0)
        {
            screen.Write($"\u001b[{previous}A");
        }

        var lines = new List<string> { useColor ? $"\u001b[1m{title}\u001b[0m" : title };
        var end = Math.Min(rows.Count, top + VisibleRows);
        for (var index = top; index < end; index++)
        {
            var more = (index == top && top > 0) ? "↑" : (index == end - 1 && end < rows.Count) ? "↓" : " ";
            lines.Add(index == selected
                ? (useColor ? $"\u001b[36;1m❯ {rows[index]}\u001b[0m {more}" : $"❯ {rows[index]} {more}")
                : $"  {rows[index]} {more}");
        }

        lines.Add(useColor ? $"\u001b[2m{Hint}\u001b[0m" : Hint);

        foreach (var line in lines)
        {
            screen.Write("\r\u001b[2K");
            screen.Write(line);
            screen.Write('\n');
        }

        screen.Flush();
        return lines.Count;
    }

    private static void Erase(TextWriter screen, int drawn)
    {
        if (drawn > 0)
        {
            screen.Write($"\u001b[{drawn}A\r\u001b[J");
        }
    }
}
