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
