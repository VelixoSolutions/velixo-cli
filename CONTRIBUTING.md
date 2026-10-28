# Contributing to the Velixo CLI

## Prerequisites

The .NET 10 SDK. A NativeAOT publish also needs the platform's native toolchain: clang and the
zlib headers on Linux, Xcode's command-line tools on macOS, the C++ build tools on Windows.

## Build, test and publish

```bash
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet publish src/Velixo.Cli -c Release -r linux-x64 -o /tmp/velixo-publish
```

The publish must print no IL or AOT warning. Every JSON type goes through `CliJsonContext` in
`src/Velixo.Cli/Serialization`, because reflection-based serialization is off in the published binary.

`dotnet test` runs on Microsoft.Testing.Platform, which `global.json` turns on; xunit v3 needs it
on the .NET 10 SDK.

Try a build without publishing it:

```bash
dotnet run --project src/Velixo.Cli -- help --all --output json
VELIXO_CONFIG_DIR=/tmp/velixo-cfg dotnet run --project src/Velixo.Cli -- login --env nx-dev --no-browser
```

## Where things go

- One command group per file in `src/Velixo.Cli/Commands/`, each with one
  `public static Command Build(CliServices services)`. `Program.BuildRootCommand` adds each group once.
- A command reads its settings, output writer and MCP client from `CliServices`. It calls
  `services.RequireIntent()` before calling a working tool, runs a task tool through
  `services.Tasks.RunAsync(...)`, and ends a failure with a `CliException`. It never writes the error
  envelope itself.
- `ToolResultUnwrapper` maps every tool result to the output document or to its exit code, so a
  server error means the same exit code in every command.
- `velixo guide --cli` embeds `../velixo-cli-docs/docs/agent-guide.md` when that checkout sits beside
  this one, and `src/Velixo.Cli/Resources/agent-guide.md` otherwise. Pass
  `-p:VelixoAgentGuide=<path>` to embed another file.
