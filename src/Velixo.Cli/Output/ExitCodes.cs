namespace Velixo.Cli.Output;

/// <summary>
/// The process exit codes of the CLI. Callers branch on these, so a value never changes meaning
/// once it has shipped.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;

    /// <summary>An unexpected failure: the network, an HTTP 5xx, a bug. Retry once, then report.</summary>
    public const int Failure = 1;

    /// <summary>A bad command line, a missing <c>--intent</c>, or a confirmation the CLI could not ask for.</summary>
    public const int Usage = 2;

    /// <summary>A builder answered with an <c>ERROR:</c> text that needs a decision or a value from the user.</summary>
    public const int BuilderRefusal = 3;

    /// <summary>Not signed in, the session ended, or the Velixo access gate refused the caller.</summary>
    public const int AuthenticationRequired = 4;

    /// <summary>The server refused the call with a coded error such as <c>SCREEN_SESSION_LOST</c>.</summary>
    public const int ServerRefused = 5;

    /// <summary>The task failed or expired on the server.</summary>
    public const int TaskFailed = 6;

    /// <summary><c>--timeout</c> elapsed while waiting; the task keeps running on the server.</summary>
    public const int Timeout = 124;

    /// <summary>Interrupted with Ctrl-C; a running task keeps running on the server.</summary>
    public const int Interrupted = 130;
}
