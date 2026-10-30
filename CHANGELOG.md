# Changelog

All notable changes to the Velixo CLI are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the version numbers follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `velixo command build --accept-defaults` lets the ERP fill every field the request does not name, as the screen would. It adds "Leave every field this request does not name at its default." to the request, which is how the builder learns that the user accepts the defaults; without it, the builder stops to ask for a branch or a project. A refusal that offers to use the defaults now names the flag in its `nextStep`.

## [0.3.1] - 2026-10-29

### Fixed

- The query viewer follows the terminal's size: resizing the window draws it again at once, with the rows and the columns laid out for the new height and width, where before it kept the old size until a key was pressed.
- The top-level help says the CLI runs ERP commands, as the `command` group is named, not screen commands.

## [0.3.0] - 2026-10-29

### Changed

- At a terminal, `query run` shows a result taller than the screen in a full-screen viewer, as `less` does, in place of the pager that printed each window below the last. The column headers stay at the top while the rows scroll: ↑ and ↓ (or `k` and `j`) move one row, PgUp, PgDn and Space one screen, Home and End go to the first and the last row, and ← and → scroll a table wider than the terminal. Rows are fetched one `--top` window at a time as they scroll into view. Esc, `q` or Enter on the last screen quits, and the rows that were on screen stay in the terminal's scrollback, with the footer that names the `--skip` for the rest. A result that fits on the screen prints as a table, as before.

## [0.2.0] - 2026-10-28

### Changed

- A file's comment has its own flag: `command run --attach PATH --attach-comment TEXT`, where each `--attach-comment` goes with the `--attach` or `--attach-token` just before it, so several files on one record keep their own comments. `--attach` takes the path alone, and the `PATH:COMMENT` and `TOKEN:COMMENT` forms are gone. A comment with no file before it, or a second comment for one file, exits 2 before anything is uploaded.

## [0.1.0] - 2026-10-28

First version. The CLI is one binary named `velixo` for Linux, macOS and Windows, published with NativeAOT. It uses the same API and the same tools as the Velixo MCP, through its own client, and exposes them as commands for people at a terminal, scripts and AI coding agents.

### Added

- A welcome screen on a bare `velixo` at a terminal: the Velixo mark, the version, the sign-in state and every command, grouped, with a one-line summary. A pipe or an agent gets the help text.
- Sign in and status: `login` (browser sign-in with PKCE on a loopback redirect, as the first-party client `velixo-cli`, with a dynamic client registration on a server that does not know it; `--no-browser` and `--complete` for a machine without a browser), `logout` (`--all`), `whoami`, `status` and `auth token`. `VELIXO_TOKEN` and `VELIXO_CLIENT_ID` for jobs that cannot sign in.
- Environments and profiles: `--env` (`prod`, `nx-staging`, `nx-stage-zero`, `nx-dev`, `local`), `--url` with a server's address (`https://feature-x.velixo.com`), `--profile`, with `config.json`, `credentials.json` (mode 0600), `state/<profile>.json`, `library/<profile>.json` and the project file `.velixo.json`. `VELIXO_URL`, `VELIXO_ENV`, `VELIXO_PROFILE` and `VELIXO_CONFIG_DIR`.
- Connections: `connections list` (`--status`, the default marked) and `connections switch` (an arrow-key picker at a terminal, `NAME` or `--clear` otherwise) for the profile's default connection. `--connection` overrides it on any command.
- The guide: `guide [COMMAND...]` prints pages embedded in the CLI, in its own terms: a get-started page with the workflow and the rules, and a page per command or group. `tools guidance` prints the server's own guidance.
- Queries: `query build` (`--base-query`, `--name`, `--save`, where a `.vql` file gets the query text), `query run` (`--plan`, `--query`, `--inquiry`, `--param`, `--params`, `--top`, `--skip`, `--all`, `--max-rows`), with warnings for a parameter label that matches nothing and a required parameter without a value, and `query workbook` (`--item`, `--download`). At a terminal, a plan prints to read, ending with the commands that run it, and results show amounts aligned and in a pager (Enter or → next, ← previous, Home, End, Esc).
- ERP commands: `command build` (`--target`, `--name`, `--save`) and `command run` (`--param`, `--param-null`, `--params`, `--rows`, `--attach`, `--attach-token`, `--attach-comment`, `--download-dir`, `--dry-run`, `--force`, `--batch FILE|-`), with local checks of the values and command sets, the write confirmation and the 10-minute duplicate guard. Continuations on Acumatica: `--leave-open`, `--continue`, `--continuation FILE` and `--finish`.
- A numbered list of built plans and batches on each profile: `query list`, `command list`, `query delete` and `command delete`; `run`, `workbook` and `delete` take an entry's number or name, and an entry runs on the connection it was built on.
- Prompts at a terminal: `query run` and `command run` ask for each value not given, and a master-detail batch for a line at a time. Scripts, pipes and agents are never asked.
- Tasks: `task status` (`--wait`), and `--no-wait` on every command that starts a task. `--timeout` is one budget for the whole command.
- Files: a file goes to the ERP by its path (`command run --attach`, `import run --file`), or is staged first with `files stage` (`--size`, `--sha256`, `--no-upload`), which uploads it or prints the URL and the `curl` command that send it, for `--attach-token` and `--file-token`. `files download` (`--out`, `--dir`, `--from`) saves what the ERP produced; a download into a directory never replaces a file.
- Import scenarios on Acumatica: `import scenarios` (`--screen`, `--filter`) and `import run` (`--scenario`, `--file`, `--file-token`, `--sheet`, `--range`, `--validate-only`, `--break-on-error`), with the validation preview before the confirmation.
- Notifications: `notify` (`--subject`, `--body`, `--target`, `--payload`, `--link`, `--link-label`, `--severity`, `--source-name`) to the portal, email, Teams or Slack.
- Discovery and configuration: `tools list` (`--names`), `help [COMMAND...] --all`, whose `--output json` prints the whole command tree, `config get`, `config set`, `config list`, `config path`, and `version` (`--check` reads the latest release from GitHub).
- Output formats `json`, `table` and `csv` (`--compact`, `--quiet`, `--no-color`), the exit codes 0, 1, 2, 3, 4, 5, 6, 124 and 130, and the JSON error document with `code`, `message`, `taskId`, `detail` and `nextStep`. `docs/commands.md` lists every error code.
- Agent detection from environment variables, which turns on non-interactive mode and goes to the server for its telemetry. The CLI sends no telemetry of its own.
- Binaries for `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64` and `linux-musl-x64`, the install scripts `install.sh` and `install.ps1`, and `checksums.txt` on every GitHub Release. The release workflow signs the Windows binaries with the Velixo EV certificate and signs and notarizes the macOS binaries once their keys are set up.

### Not in this version

- Sign-in with a device code. `login --complete` takes the redirected URL until the gateway has the device grant.
- An automatic update check, and tokens in the operating system's credential store.
- Package managers: Homebrew, Scoop, WinGet, apt, yum and the Docker image.

[0.3.1]: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v0.3.1
[0.3.0]: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v0.3.0
[0.2.0]: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v0.2.0
[0.1.0]: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v0.1.0
