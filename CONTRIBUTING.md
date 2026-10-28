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
- A relative path from the command line resolves against `services.Environment.CurrentDirectory`, never
  the process's current directory, so tests can run commands in a temporary folder.
- A command reads its settings, output writer and MCP client from `CliServices`. It calls
  `services.IntentOrDefault()` for the intent of a working tool, runs a task tool through
  `services.Tasks.RunAsync(..., services.FollowOptions(), ...)` so that every call of the command shares
  one `--timeout`, asks for a write's go-ahead with `Confirmation.Require`, and ends a failure with a
  `CliException`. It never writes the error envelope itself.
- `ToolResultUnwrapper` maps every tool result to the output document or to its exit code, so a
  server error means the same exit code in every command: a failed task whose error starts with
  `ERROR:` is exit 3, one that starts with a server code is exit 5, any other is exit 6. A command
  does not re-map them.
- `velixo guide --cli` embeds `docs/agent-guide.md`, and `src/Velixo.Cli/Resources/agent-guide.md`
  only in a build without the `docs` folder. Pass `-p:VelixoAgentGuide=<path>` to embed another file.
- Every error code the CLI raises is a constant in `Output/ErrorCodes.cs`, and `Mcp/ErrorGuidance.cs`
  holds the `nextStep` of each server code. `docs/commands.md` lists every code with its exit code.
- Files move through `Files/FileTransfer.cs`: staging, uploading and downloading. A download into a
  directory never replaces a file; a download to an exact path does.

## The symbol on the welcome screen

`src/Velixo.Cli/Output/VelixoSymbol.cs` is generated from the brand artwork in `assets/`: the colours
come from `symbol-gradient.svg`, the pixels drawn from `symbol-flat-black.svg`, whose gaps keep the
weave readable at 24 pixels. To regenerate it:

```bash
python3 -m pip install pillow
npm install playwright            # or any installed Chromium that playwright finds
node scripts/render-svg.cjs assets/symbol-gradient.svg /tmp/gradient.png
node scripts/render-svg.cjs assets/symbol-flat-black.svg /tmp/flat.png
python3 scripts/render-symbol.py /tmp/gradient.png 24 --shape /tmp/flat.png
```

`render-svg.cjs` crops nothing: both files share the same canvas, so the two renders line up.
