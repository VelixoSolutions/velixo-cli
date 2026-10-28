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
}
