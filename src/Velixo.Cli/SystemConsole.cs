namespace Velixo.Cli;

public sealed class SystemConsole : ICliConsole
{
    public TextWriter Out => Console.Out;

    public TextWriter Error => Console.Error;

    public TextReader In => Console.In;

    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public bool IsErrorRedirected => Console.IsErrorRedirected;

    public bool IsInputRedirected => Console.IsInputRedirected;

    public int WindowWidth
    {
        get
        {
            if (Console.IsOutputRedirected)
            {
                return 0;
            }

            try
            {
                return Console.WindowWidth;
            }
            catch (IOException)
            {
                return 0;
            }
        }
    }

    public int WindowHeight
    {
        get
        {
            if (Console.IsOutputRedirected)
            {
                return 0;
            }

            try
            {
                return Console.WindowHeight;
            }
            catch (IOException)
            {
                return 0;
            }
        }
    }

    public bool KeyAvailable
    {
        get
        {
            try
            {
                return Console.KeyAvailable;
            }
            catch (InvalidOperationException)
            {
                // Without a terminal to poll, ReadKey decides.
                return true;
            }
        }
    }

    public ConsoleKeyInfo ReadKey()
    {
        var treatControlCAsInput = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;
        try
        {
            return Console.ReadKey(intercept: true);
        }
        finally
        {
            Console.TreatControlCAsInput = treatControlCAsInput;
        }
    }
}
