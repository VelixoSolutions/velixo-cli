namespace Velixo.Cli.IntegrationTests;

/// <summary>Names the commands a live test runs, such as <c>query build</c>, so a test can check that every command has one.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CoversAttribute(params string[] commands) : Attribute
{
    public IReadOnlyList<string> Commands { get; } = commands;
}

/// <summary>
/// Names the flags of one command that a live test passes, such as <c>[CoversFlags("query run", "--skip", "--all")]</c>,
/// so a test can check that every flag has one. Repeat it once per command. The shared flags, which every
/// command accepts, are named under <see cref="Shared"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CoversFlagsAttribute(string command, params string[] flags) : Attribute
{
    /// <summary>The command name that stands for the shared flags, such as <c>--timeout</c> and <c>--compact</c>.</summary>
    public const string Shared = "*";

    public string Command { get; } = command;

    public IReadOnlyList<string> Flags { get; } = flags;
}
