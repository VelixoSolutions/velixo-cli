namespace Velixo.Cli.Output;

/// <summary>
/// The one way a command ends with a non-zero exit. <c>Program</c> turns it into the error envelope
/// on stdout in JSON mode, or into text on stderr otherwise.
/// </summary>
public sealed class CliException : Exception
{
    public CliException(int exitCode, string code, string message, string? nextStep = null)
        : base(message)
    {
        ExitCode = exitCode;
        Code = code;
        NextStep = nextStep;
    }

    public CliException(int exitCode, string code, string message, string? nextStep, Exception innerException)
        : base(message, innerException)
    {
        ExitCode = exitCode;
        Code = code;
        NextStep = nextStep;
    }

    public int ExitCode { get; }

    /// <summary>Gets the machine-readable code: a server code such as <c>SCREEN_SESSION_LOST</c>, or the CLI's own.</summary>
    public string Code { get; }

    /// <summary>Gets the one-line recovery the caller should take, when the CLI knows one.</summary>
    public string? NextStep { get; }

    public string? TaskId { get; init; }

    /// <summary>Gets the failure detail a task or the ERP reported, beside the message.</summary>
    public string? Detail { get; init; }

    public static CliException Usage(string message, string? nextStep = null) =>
        new(ExitCodes.Usage, ErrorCodes.Usage, message, nextStep);

    public static CliException NotSignedIn(string profile) =>
        new(
            ExitCodes.AuthenticationRequired,
            ErrorCodes.NotSignedIn,
            $"You are not signed in to Velixo on the profile '{profile}'.",
            $"velixo login --profile {profile}");
}
