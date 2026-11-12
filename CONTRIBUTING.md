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

## Help Center documentation

The customer documentation lives in Confluence, in the Help Center space (HC), under
[Velixo CLI documentation](https://velixo.atlassian.net/wiki/spaces/HC/pages/1447329855/Velixo+CLI+documentation).
Scroll Sites publishes it at [help.velixo.com](https://help.velixo.com/doc/main/velixo-cli/velixo-cli-documentation).
It is written from this repository's `README.md`, `docs/commands.md` and `docs/guide/`, so a change to a
command, a flag or an output means a page to update there as well.

- **Label every page `cli`.** Scroll Sites publishes only the pages that carry the label. A page
  without it stays in Confluence and never reaches help.velixo.com.
- **Build the hierarchy from pages and subpages, never folders.** Scroll Sites builds the site's
  navigation from the page tree: put a page under its parent page, not in a Confluence folder.
- **Write macros in ADF, not storage HTML or Markdown.** Info and warning panels, the excerpt and the
  table of contents only render when they are native ADF nodes. When Claude writes a page through the
  Atlassian MCP, its HTML body format converts to ADF (`<div data-type="panel-warning">` for a panel);
  Confluence storage XML (`<ac:structured-macro>`) shows up as raw text.
- **Follow the Help Center's house style.** Open each page with a hidden excerpt macro named `Summary`
  holding one sentence, then an `Overview` heading that ends with "The article covers:" and a list of
  the sections. Use gerund titles and headings ("Querying ERP data with the Velixo CLI") and end with
  `Related articles`. Call the server the Velixo AI Gateway, not the MCP server.
- **Write for customers.** Leave out the internal environments (`nx-staging`, `nx-stage-zero`,
  `nx-dev`, `local`) and `--env`, ticket numbers and internal connection names, and use neutral
  examples such as `acumatica-prod`.
- **Add the label last.** Create or fill a page first and label it once its content is final, so an
  empty or half-written page is never published.

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
then tag: `git tag -a v1.1.0 -m "Velixo CLI 1.1.0" && git push origin v1.1.0`. The Release workflow
tests, builds the seven targets with NativeAOT, signs, packages, checksums and publishes them.

The repository's releases must stay public. `install.sh` downloads from GitHub Releases, so when the
repository is private or internal, the script fails with 404 for everyone outside the organization.

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

### Homebrew, WinGet and npm

When a Release run for a tag succeeds, the Package managers workflow (`package-managers.yml`)
starts by itself and publishes that release to all three. A prerelease (a tag with a hyphen) goes to
none. To publish a release that is already out, run Package managers by hand (Actions, Package
managers, Run workflow) with its tag.

- **Homebrew:** this repository is the tap. The job writes `Formula/velixo.rb` with
  `scripts/homebrew-formula.sh` from the release's `checksums.txt`, installs the CLI with it on a
  macOS runner, and commits it to `main`. Do not edit the formula by hand.
- **WinGet:** the package `Velixo.CLI` is in `microsoft/winget-pkgs`. The job opens a pull request
  there with Komac, through `vedantmgoyal9/winget-releaser`, from the token owner's fork. It needs
  the organization secret `WINGET_TOKEN`: a classic token with `public_repo` (a fine-grained token
  cannot open the pull request). Without the secret, the job skips with a warning. Until
  `Velixo.CLI` is in `winget-pkgs`, the job writes the manifests with `scripts/winget-manifests.sh`
  and submits them as a new package with `komac submit`; while that pull request is open, it skips.
- **npm:** `@velixo/cli` holds a launcher, `npm/cli/bin/velixo.js`, and lists seven platform
  packages, `@velixo/cli-<platform>`, as optional dependencies; npm installs the one whose `os`,
  `cpu` and `libc` match. `velixo-cli` and `velixo` are aliases that install `@velixo/cli`, so
  nobody else can publish under those names. `scripts/npm-packages.mjs` builds all ten from the
  release's archives, and the job publishes the platform packages first, then `@velixo/cli`, then
  the aliases, skipping a version already published. It
  publishes with npm's trusted publishing, with no npm token: each package names
  `package-managers.yml` as its trusted publisher. Until `@velixo/cli` exists on the registry, the
  job skips with a warning.

The first npm version goes in by hand, once, from a machine signed in to npm (`npm login`) as an
owner of the `@velixo` organization. In a clone of this repository:

```sh
gh release download v1.0.2 --repo VelixoSolutions/velixo-cli --pattern 'velixo-*' --dir /tmp/velixo-dist
node scripts/npm-packages.mjs 1.0.2 /tmp/velixo-dist /tmp/velixo-npm
for dir in /tmp/velixo-npm/cli-* /tmp/velixo-npm/cli /tmp/velixo-npm/alias-*; do (cd "$dir" && npm publish --access public); done
```

npm asks for your second factor for each package. Then, on npmjs.com, open each of the ten
packages (`@velixo/cli`, the seven `@velixo/cli-<platform>`, `velixo-cli` and `velixo`), Settings, Trusted Publisher,
GitHub Actions, and enter organization `VelixoSolutions`, repository `velixo-cli`, workflow
filename `package-managers.yml`, and no environment. Under PublishiThe first WinGet version also comes from the workflow. Once `WINGET_TOKEN` is set, run Package
managers by hand with the tag of the latest release: the job submits `Velixo.CLI` as a new package.
Microsoft reviews it in 1 to 3 days. Until it is merged, the job skips WinGet, so if a release goes
out in the meantime, run Package managers again with its tag after the merge.

 alias, then let it submit
the pull request. Microsoft reviews it in 1 to 3 days; every later version goes through the
workflow.

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
