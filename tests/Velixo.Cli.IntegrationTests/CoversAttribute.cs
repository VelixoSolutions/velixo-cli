namespace Velixo.Cli.IntegrationTests;

/// <summary>Names the commands a live test runs, such as <c>query build</c>, so a test can check that every command has one.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CoversAttribute(params string[] commands) : Attribute
{
    public IReadOnlyList<string> Commands { get; } = commands;
}
