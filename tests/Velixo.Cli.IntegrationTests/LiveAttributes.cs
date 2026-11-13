using System.Runtime.CompilerServices;

namespace Velixo.Cli.IntegrationTests;

/// <summary>A test against the live environment, skipped unless <c>VELIXO_IT_ENV</c> names one.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = "Set VELIXO_IT_ENV to run the live tests.";
        SkipUnless = nameof(LiveCli.Enabled);
        SkipType = typeof(LiveCli);
    }
}

/// <summary>A test against the live environment for each of its data rows, such as each <see cref="Erp"/>, skipped unless <c>VELIXO_IT_ENV</c> names one.</summary>
public sealed class LiveTheoryAttribute : TheoryAttribute
{
    public LiveTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = "Set VELIXO_IT_ENV to run the live tests.";
        SkipUnless = nameof(LiveCli.Enabled);
        SkipType = typeof(LiveCli);
    }
}
