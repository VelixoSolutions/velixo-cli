using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Query;

/// <summary>
/// Gives a builder's refusal its own exit code whichever way it arrives. The server fails the build
/// task with the <c>ERROR:</c> text as its error, which <see cref="ToolResultUnwrapper"/> reads as a
/// failed task, exit 6; a refusal asks the user for a decision or a value, so it is exit 3.
/// </summary>
internal static class BuilderRefusal
{
    private const string Prefix = "ERROR:";

    public static async Task<UnwrappedToolResult> MapAsync(Task<UnwrappedToolResult> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        try
        {
            return await call.ConfigureAwait(false);
        }
        catch (CliException failed) when (failed.Code == ErrorCodes.TaskFailed && failed.Message.TrimStart().StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CliException(
                ExitCodes.BuilderRefusal,
                ErrorCodes.BuilderRefusal,
                failed.Message.Trim(),
                "Put the builder's question to the user in plain words, then build again with their answer.",
                failed)
            {
                TaskId = failed.TaskId,
            };
        }
    }
}
