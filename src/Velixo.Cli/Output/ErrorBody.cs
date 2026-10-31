using System.Text.Json.Serialization;

namespace Velixo.Cli.Output;

public sealed class ErrorBody
{
    [JsonPropertyName("code")]
    required public string Code { get; init; }

    [JsonPropertyName("exitCode")]
    required public int ExitCode { get; init; }

    /// <summary>Gets the server's text verbatim, or the CLI's own message for an error it raised.</summary>
    [JsonPropertyName("message")]
    required public string Message { get; init; }

    [JsonPropertyName("taskId")]
    public string? TaskId { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("nextStep")]
    public string? NextStep { get; init; }
}
