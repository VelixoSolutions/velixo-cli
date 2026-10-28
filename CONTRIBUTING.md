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
- `velixo guide` prints the pages in `docs/guide/`, embedded at build time. A page's first line,
  `<!-- commands: query build -->`, names the commands it covers. `GuideCommandTests` fails when a
  command has no page, or when a page mentions a command or a flag that does not exist, so a new
  command or a renamed flag means a page to update.
- Every error code the CLI raises is a constant in `Output/ErrorCodes.cs`, and `Mcp/ErrorGuidance.cs`
  holds the `nextStep` of each server code. `docs/commands.md` lists every code with its exit code.
- Files move through `Files/FileTransfer.cs`: staging, uploading and downloading. A download into a
  directory never replaces a file; a download to an exact path does.

## Live tests

`tests/Velixo.Cli.IntegrationTests` runs the real binary against a live environment and an ERP
connection, and covers every command: each test names the commands it runs in `[Covers(...)]`, and
`Every_command_has_a_live_test` fails when a command has none. Nothing writes to the ERP: a write
batch runs only with `--dry-run` and without `--yes`, an import only with `--validate-only`. `notify`
sends one message, to the signed-in user. `connections switch` puts the profile's default back, and
`config`, `login` and `logout` run in a configuration folder of their own. Without `VELIXO_IT_ENV`
the live tests skip, so `dotnet test` stays offline.

On your machine, keep a profile for the tests, sign it in once, and run them:

```bash
velixo login --profile it --env nx-dev
VELIXO_IT_ENV=nx-dev dotnet test tests/Velixo.Cli.IntegrationTests -c Release
```

`VELIXO_IT_PROFILE` names another profile, `VELIXO_IT_CONNECTION` another connection (default
`gab-dev`), and `VELIXO_IT_BINARY` another binary, such as a NativeAOT publish. The tests run one at a
time: each run of the binary can rotate the refresh token, and two rotations of one token at once end
the session.

In CI, `.github/workflows/integration.yml` runs them nightly and on demand against the NativeAOT
binary, signed in with the refresh token in `secrets.VELIXO_IT_TOKEN`, and saves the rotated token
back into the secret. Its header says how to seed the token and which settings it needs.

## Releases

Bump `<Version>` in `Directory.Build.props`, date the version's section in `CHANGELOG.md`, push,
then tag: `git tag -a v0.2.0 -m "Velixo CLI 0.2.0" && git push origin v0.2.0`. The Release workflow
tests, builds the seven targets with NativeAOT, signs, packages, checksums and publishes them.

`velixo.exe` is Authenticode-signed with the Velixo EV certificate in Azure Key Vault, with
AzureSignTool, as Velixo Classic is; `.github/code-signing.json` names the certificate and mirrors
`build-scripts/code-signing.json` in Velixo-NextGen. The organization secret
`VELIXO_AZURE_KEY_VAULT_CLIENT_SECRET` must be shared with this repository.

The macOS binaries are signed with the Velixo Developer ID Application certificate and the
hardened runtime, then notarized, from five secrets: `APPLE_CERTIFICATE_P12_BASE64`,
`APPLE_CERTIFICATE_PASSWORD`, `APPLE_API_KEY_ID`, `APPLE_API_ISSUER_ID` and
`APPLE_API_KEY_P8_BASE64` (an App Store Connect API key). `release.yml` describes each.

A target whose keys are missing fails the release rather than ship unsigned. Until the keys
exist, set the repository variable `VELIXO_ALLOW_UNSIGNED` to `true`
(`gh variable set VELIXO_ALLOW_UNSIGNED --body true`): the release then ships those targets
unsigned, with a warning in the run. Delete the variable once every key is in place.

To check signing before a release, run the Release workflow by hand (Actions, Release, Run
workflow): it builds and signs every target and keeps the packages as run artifacts, and
publishes nothing. Tick "allow unsigned" to build the targets whose keys are missing.

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
