# Changelog

All notable changes to the Velixo CLI are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the version numbers follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.1.0] - 2026-11-14

### Added

- Links to ERP records. At a terminal, the cells of a `query run` table that name a record open it on a click (a terminal hyperlink), in the table and in the full-screen viewer, as the Velixo Intelligence chat does. A `command run` table that saved a record ends with its link, as `Saved SO SO005396: https://…`; on Sage Intacct it names the record by its ID, such as the vendor ID, not by its internal record number. Acumatica and Sage Intacct; Business Central has no links. A link opens on a click only in a terminal that supports OSC 8 hyperlinks, such as Windows Terminal, the Visual Studio Code terminal, iTerm2, or Ghostty; the default macOS Terminal app shows the text with no link. (RD-14754)
- `--links` asks a query for the links in JSON (`result.data.recordLinks`, as the server sends them) and CSV (a `URL` column beside each linked column) too. An agent or a pipe gets no links unless it passes `--links`, since they cost the server time. `--all` keeps the links of every window. With `--links`, a table that cannot show a hyperlink (not at a terminal, `TERM=dumb`, or a legacy Windows console) shows the URLs in a column instead; without it, such a table asks for no links.

### Fixed

- `login --complete` stops when `--timeout` runs out, with exit 124 and the command that goes on waiting. It used to wait until the code expired, whatever `--timeout` said. The pending sign-in stays, so running `login --complete` again can still finish it.
- `query run` and `query workbook` no longer say that a query with a required parameter left out "matches nothing". Depending on the query, it can also ignore that filter and return too many rows, so the warning now says the query either ignores the filter or returns no rows.

## [1.0.5] - 2026-11-10

### Fixed

- Every command reads a task's answer from `response`, the field the server sends it in since RD-13757, as well as from `textualResponse`, the field before. Against a server with that change, 1.0.4 found no answer: `query build` printed a one-row `response` table instead of the plan, `query run` printed the raw document instead of the rows, a builder refusal exited 0, and `import run` lost its counts. (RD-13757)

## [1.0.4] - 2026-11-07

### Changed

- Nothing in the CLI itself. This release checks that every package manager publishes from the release workflow alone.

## [1.0.3] - 2026-11-07

### Added

- Homebrew, WinGet and npm. On macOS and Linux, `brew tap velixosolutions/velixo-cli https://github.com/VelixoSolutions/velixo-cli` then `brew install velixo`; on Windows, `winget install Velixo.CLI`; anywhere, `npm install -g @velixo/cli`. Each release updates all three. (RD-14780)
- `velixo version --check` prints `updateCommand`, the command that updates the CLI for the way it was installed: `brew upgrade velixo`, `winget upgrade Velixo.CLI`, `npm install -g @velixo/cli@latest` or the install script. At a terminal it shows it when a newer release is out.
- A spinner on stderr while the CLI waits at a terminal: on the server, on a task (its progress line redraws in place instead of printing again after each poll), and on a sign-in in the browser or with a device code. It shows only after a quarter of a second, erases itself when the wait ends, and keeps progress and warnings printed above it. A script, a pipe, an AI agent, `--quiet`, `--non-interactive` and `TERM=dumb` see no spinner and get the progress lines as before.

## [1.0.2] - 2026-11-07

### Changed

- The macOS binaries are signed with the Velixo Developer ID Application certificate and the hardened runtime, and notarized by Apple, so Gatekeeper opens `velixo` without a warning. `velixo.exe` is signed with the Velixo EV certificate. The CLI itself is unchanged from 1.0.1.

## [1.0.1] - 2026-11-04

### Changed

- **Breaking:** `velixo login --no-browser` signs in with an OAuth device code (RFC 8628). It prints a URL and a short code; the person enters the code in a browser on any device, and nothing is pasted back. At a terminal the command waits for the sign-in. Without one, it prints `authorization_required` with `verificationUri` and `userCode` in place of `browserUrl`, and `velixo login --complete`, which no longer takes a value, waits for the sign-in. It shows the short verification URL, and the person types the code there. It needs a server that publishes a `device_authorization_endpoint`.
- `velixo login` signs in as `--no-browser` does when no browser the person would see can be opened: in an SSH session, on Linux without a graphical display, or without `xdg-open`. It says why on stderr. A `BROWSER` variable, as a remote editor sets to forward to your own machine, keeps the browser sign-in, and the CLI now opens the page with the command it names.

### Fixed

- `query build` and `command build` exit 3 with `BUILDER_REFUSAL` when the server finishes the build with a `{"question": "ERROR: …"}` answer, the shape it uses since RD-14621. The question is in `error.message`, `command build` shows the `--accept-defaults` hint when the question offers the defaults, and `--save` writes nothing. Before, the CLI exited 0 and saved the refusal as a plan or a batch. A build answer that holds nothing but a `question` counts too, whatever its text, and so does any result the server marks `refused: true` (RD-14934). (RD-14834)

## [1.0.0] - 2026-10-30

Initial release. The CLI is one binary named `velixo` for Linux, macOS and Windows, published with NativeAOT. It uses the same API and the same tools as the Velixo MCP, through its own client, and exposes them as commands for people at a terminal, scripts and AI coding agents.

### Added

- A welcome screen on a bare `velixo` at a terminal: the Velixo mark, the version, the sign-in state and every command, grouped, with a one-line summary. A pipe or an agent gets the help text.
- Sign in and status: `login` (browser sign-in with PKCE on a loopback redirect, as the first-party client `velixo-cli`, with a dynamic client registration on a server that does not know it), `logout` (`--all`), `whoami`, `status` and `auth token`. `login --no-browser` at a terminal waits for the address the browser lands on and finishes the sign-in, so a server or a container signs in with one command; without a terminal it prints the URL and the `--complete` command. `VELIXO_TOKEN` and `VELIXO_CLIENT_ID` for jobs that cannot sign in.
- Environments and profiles: `--env` (`prod`, `nx-staging`, `nx-stage-zero`, `nx-dev`, `local`), `--url` with a server's address (`https://feature-x.velixo.com`), `--profile`, with `config.json`, `credentials.json` (mode 0600), `state/<profile>.json`, `library/<profile>.json` and the project file `.velixo.json`. `VELIXO_URL`, `VELIXO_ENV`, `VELIXO_PROFILE` and `VELIXO_CONFIG_DIR`.
- Connections: `connections list` (`--status`, the default marked, and each connection's `company` and `url` when the server returns them) and `connections switch` (an arrow-key picker at a terminal, `NAME` or `--clear` otherwise) for the profile's default connection. `--connection` overrides it on any command.
- The guide: `guide [COMMAND...]` prints pages embedded in the CLI, in its own terms: a get-started page with the workflow and the rules, and a page per command or group. `tools guidance` prints the server's own guidance.
- Queries: `query build` (`--base-query`, `--name`, `--save`, where a `.vql` file gets the query text), `query run` (`--plan`, `--query`, `--inquiry`, `--param`, `--params`, `--top`, `--skip`, `--all`, `--max-rows`), with warnings for a parameter label that matches nothing and a required parameter without a value, and `query workbook` (`--item`, `--download`). A plan of one query prints `{ taskId, status, result }` however it is run; a plan of several prints `{ note, results }`. At a terminal, a result taller than the screen opens in a full-screen viewer that keeps the column headers at the top, scrolls by row, screen and column, fetches rows one `--top` window at a time, and follows the terminal's size.
- ERP commands: `command build` (`--target`, `--name`, `--save`, `--accept-defaults`) and `command run` (`--param`, `--param-null`, `--params`, `--rows`, `--attach`, `--attach-token`, `--attach-comment`, `--download-dir`, `--dry-run`, `--batch FILE|-`), with local checks of the values and command sets and the write confirmation. A batch that only reads runs without `--yes`. Continuations on Acumatica: `--leave-open`, `--continue`, `--continuation FILE` and `--finish`.
- A numbered list of built plans and batches on each profile: `query list`, `command list`, `query delete` and `command delete`; `run`, `workbook` and `delete` take an entry's number or name, and an entry runs on the connection it was built on.
- Prompts at a terminal: `query run` and `command run` ask for each value not given, and a master-detail batch for a line at a time. Scripts, pipes and agents are never asked.
- Tasks: `task status` (`--wait`), and `--no-wait` on every command that starts a task. `--timeout` is one budget for the whole command.
- Files: a file goes to the ERP by its path (`command run --attach`, `import run --file`), or is staged first with `files stage` (`--size`, `--sha256`, `--no-upload`), which uploads it or prints the URL and the `curl` command that send it, for `--attach-token` and `--file-token`. `files download` (`--out`, `--dir`, `--from`) saves what the ERP produced; a download into a directory never replaces a file.
- Import scenarios on Acumatica: `import scenarios` (`--screen`, `--filter`) and `import run` (`--scenario`, `--file`, `--file-token`, `--sheet`, `--range`, `--validate-only`, `--break-on-error`), with the validation preview before the confirmation.
- Notifications: `notify` (`--subject`, `--body`, `--target`, `--payload`, `--link`, `--link-label`, `--severity`, `--source-name`) to the portal, email, Teams or Slack.
- Discovery and configuration: `tools list` (`--names`), `help [COMMAND...] --all`, whose `--output json` prints the whole command tree, `config get`, `config set`, `config list`, `config path`, and `version` (`--check` reads the latest release from GitHub).
- Several commands at once on one profile: one process renews the access token while the others wait for it, and every change to the CLI's files is kept. A file held by another velixo process for more than 30 seconds gives `FILE_LOCKED`.
- Output formats `json`, `table` and `csv` (`--compact`, `--quiet`, `--no-color`), the exit codes 0, 1, 2, 3, 4, 5, 6, 124 and 130, and the JSON error document with `code`, `message`, `taskId`, `detail` and `nextStep`. `docs/commands.md` lists every error code.
- Agent detection from environment variables, which turns on non-interactive mode and goes to the server for its telemetry. The CLI sends no telemetry of its own.
- Binaries for `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64` and `linux-musl-x64`, the install scripts `install.sh` and `install.ps1`, and `checksums.txt` on every GitHub Release. The release workflow signs the Windows binaries with the Velixo EV certificate and signs and notarizes the macOS binaries once their keys are set up.

[1.1.0]: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v1.1.0
[1.0.5]: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v1.0.5
[1.0.4]: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v1.0.4
[1.0.3]: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v1.0.3
[1.0.2]: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v1.0.2
[1.0.1]: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v1.0.1
[1.0.0]: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v1.0.0
