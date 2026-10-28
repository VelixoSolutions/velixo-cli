# Changelog

All notable changes to the Velixo CLI are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the version numbers follow [Semantic Versioning](https://semver.org/).

## [0.1.0] - Unreleased

First version. The CLI is one binary named `velixo` for Linux, macOS and Windows, published with NativeAOT. It calls the Velixo MCP server over its own Streamable HTTP client and exposes the server's tools as commands.

### Added

- A welcome screen on a bare `velixo` in a terminal: the Velixo mark, the version, the sign-in state and the first commands. A pipe or an agent gets the help text.

- Sign in and status: `login` (browser sign-in with PKCE on a loopback redirect, `--no-browser` and `--complete` for a machine without a browser, dynamic client registration once per environment), `logout` (`--all`), `whoami`, `status` and `auth token`. `VELIXO_TOKEN` and `VELIXO_CLIENT_ID` for jobs that cannot sign in.
- Environments and profiles: `--env` (`prod`, `nx-staging`, `nx-stage-zero`, `nx-dev`, `local`), `--url`, `--profile`, with `config.json`, `credentials.json` (mode 0600), `state/<profile>.json` and the project file `.velixo.json`. `VELIXO_CONFIG_DIR` moves the folder.
- Connections and guidance: `connections list` (`--status`), `guide [TOPIC...]` for the server's tool guidance and `guide --cli` for the embedded agent guide.
- Queries: `query build` (`--base-query`), `query run` (`--plan`, `--query`, `--inquiry`, `--param`, `--params`, `--top`, `--skip`, `--all`, `--max-rows`), with warnings for a parameter label that matches nothing and a required parameter without a value, and `query workbook` (`--item`, `--download`).
- Screen commands on Acumatica: `screen build` (`--target`) and `screen run` (`--var`, `--var-null`, `--vars`, `--rows`, `--attach PATH[:COMMENT]`, `--download-dir`, `--dry-run`, `--force`), with local checks of variables and command sets, the write confirmation and the 10-minute duplicate guard.
- Continuations: `--leave-open`, `--continue`, `--continuation FILE` and `--finish`, with local checks of the MCP session, the deadline and the screen.
- Tasks: `task status` (`--wait`), and `--no-wait` on every command that starts a task. `--timeout` is one budget for the whole command.
- Files: `files upload` (`--comment`), `files request` (`--max-files`), `files staged` (`--wait`) and `files download` (`--out`, `--dir`, `--from`). A download into a directory never replaces a file.
- Import scenarios on Acumatica: `import scenarios` (`--screen`, `--filter`) and `import run` (`--scenario`, `--file`, `--sheet`, `--range`, `--validate-only`, `--break-on-error`), with the validation preview before the confirmation.
- Notifications: `notify` (`--subject`, `--body`, `--target`, `--payload`, `--link`, `--link-label`, `--severity`, `--source-name`).
- Raw access and discovery: `tools list` (`--names`), `call` (`--args JSON`, `--args @FILE`, `--stdin`) and `help [COMMAND...] --all`, whose `--output json` prints the whole command tree.
- Configuration: `config get`, `config set`, `config list`, `config path`, and `version` (`--check` reads the latest release from GitHub).
- Output formats `json`, `table` and `csv` (`--compact`, `--quiet`, `--no-color`), the exit codes 0, 1, 2, 3, 4, 5, 6, 124 and 130, and the JSON error document with `code`, `message`, `taskId`, `detail` and `nextStep`. `docs/commands.md` lists every error code.
- Agent detection from environment variables, which turns on non-interactive mode and goes to the server for its telemetry. The CLI sends no telemetry of its own.
- Binaries for `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64` and `linux-musl-x64`, the install scripts `install.sh` and `install.ps1`, and `checksums.txt` on every GitHub Release.

### Not in this version

- `velixo agent setup`: the command exists and exits 2 with `NOT_IMPLEMENTED`.
- Sign-in with a device code. `login --complete` takes the redirected URL until the gateway has the device grant.
- A pager, an update check, and tokens in the operating system's credential store.
- Package managers: Homebrew, Scoop, WinGet, apt, yum and the Docker image.

[0.1.0]: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v0.1.0
