using System.Text.Json.Serialization;

namespace Velixo.Cli.Output;

/// <summary>The document stdout holds on every non-zero exit in JSON mode: <c>{ "error": { ... } }</c>.</summary>
public sealed class ErrorEnvelope
{
    [JsonPropertyName("error")]
    required public ErrorBody Error { get; init; }

    public static ErrorEnvelope From(CliException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new ErrorEnvelope
        {
            Error = new ErrorBody
            {
                Code = exception.Code,
                ExitCode = exception.ExitCode,
                Message = exception.Message,
                TaskId = exception.TaskId,
                Detail = exception.Detail,
                NextStep = exception.NextStep,
            },
        };
    }
}
