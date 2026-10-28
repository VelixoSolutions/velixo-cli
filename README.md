<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://secure.velixo.com/logos/logo-full-gradient-white.svg">
    <img src="https://secure.velixo.com/logos/logo-full-gradient-navy.svg" alt="Velixo" width="240">
  </picture>
</p>

# Velixo CLI

The Velixo CLI runs the tools of the Velixo MCP server from the command line, so AI coding agents and scripts can query an ERP, run screen commands, move files and run imports without an MCP host.

[![Latest release](https://img.shields.io/github/v/release/VelixoSolutions/velixo-cli)](https://github.com/VelixoSolutions/velixo-cli/releases/latest)
[![CI](https://github.com/VelixoSolutions/velixo-cli/actions/workflows/ci.yml/badge.svg)](https://github.com/VelixoSolutions/velixo-cli/actions/workflows/ci.yml)

The CLI is one binary named `velixo` for Linux, macOS and Windows. It needs no runtime on your machine. It is a thin client: it calls the same Velixo MCP server and the same tools as Claude, ChatGPT and Teams, so the CLI and the server cannot drift apart.

The CLI is for agents that have a shell (Claude Code, Codex, Cursor, Gemini CLI), for CI jobs and for people who prefer a terminal. Hosted chat clients that cannot run a binary keep using the MCP server.

## Before you start

You need three things:

1. A Velixo account on the Velixo portal.
2. The `mcpServerEnabled` feature switch, turned on for you by a Velixo administrator.
3. Velixo Intelligence credits.

Without all three, commands that call the server exit with code 4 and print the reason.

## Installation

The CLI runs on Linux (x64, arm64, and x64 with musl, such as Alpine), macOS (Intel and Apple silicon) and Windows (x64 and arm64).

### Install script (macOS, Linux)

```sh
curl -fsSL https://cli.velixo.com/install.sh | sh
```

The script detects your operating system and processor, downloads the matching file from the latest GitHub Release, checks its SHA-256 sum against `checksums.txt` and installs `velixo` to `~/.local/bin`. Set `VELIXO_INSTALL_DIR` to install somewhere else.

To install a specific version, pass it as the first argument:

```sh
curl -fsSL https://cli.velixo.com/install.sh | sh -s -- 0.1.0
```

### Install script (Windows PowerShell)

```powershell
irm https://cli.velixo.com/install.ps1 | iex
```

The script installs `velixo.exe` to `%LOCALAPPDATA%\Programs\velixo` and adds that folder to your user `PATH`. Open a new terminal afterwards.

To install a specific version:

```powershell
& ([scriptblock]::Create((irm https://cli.velixo.com/install.ps1))) -Version 0.1.0
```

### GitHub Releases

Download the file for your platform from the [latest release](https://github.com/VelixoSolutions/velixo-cli/releases/latest), together with `checksums.txt`.

| Platform | File |
|---|---|
| Linux x64 | `velixo-<version>-linux-x64.tar.gz` |
| Linux arm64 | `velixo-<version>-linux-arm64.tar.gz` |
| Linux x64, musl (Alpine) | `velixo-<version>-linux-musl-x64.tar.gz` |
| macOS Intel | `velixo-<version>-osx-x64.tar.gz` |
| macOS Apple silicon | `velixo-<version>-osx-arm64.tar.gz` |
| Windows x64 | `velixo-<version>-win-x64.zip` |
| Windows arm64 | `velixo-<version>-win-arm64.zip` |

Check the sum, extract the archive and move the binary to a folder on your `PATH`:

```sh
sha256sum --check --ignore-missing checksums.txt
tar -xzf velixo-0.1.0-linux-x64.tar.gz
mv velixo ~/.local/bin/
```

On Windows, unzip the archive and add the folder that holds `velixo.exe` to your `Path` variable.

### Package managers (coming soon)

These channels do not exist yet. Use the install scripts or GitHub Releases for now.

| Channel | Planned command |
|---|---|
| Homebrew | `brew install velixosolutions/tap/velixo` |
| Scoop | `scoop bucket add velixo` then `scoop install velixo` |
| WinGet | `winget install Velixo.CLI` |
| apt (Debian, Ubuntu) | signed repository, commands to follow |
| yum and dnf (RedHat, Fedora, CentOS) | signed repository, commands to follow |
| Docker | `docker run --rm -it velixo/cli version` |

### Upgrading

The CLI does not update itself and runs no update check. Run `velixo version --check` to compare with the latest release, then run the install script again or download the new release.

### Uninstalling

1. Run `velixo logout --all` to end your sessions and delete the stored tokens.
2. Delete the `velixo` binary.
3. Delete the configuration folder if you want to remove your settings (see [Configuration files](#configuration-files)).

## Sign in

```sh
velixo login
```

The command opens your browser, you sign in with Microsoft, Google or an emailed code and choose your organization, and the CLI stores the tokens. It uses OAuth with PKCE on a loopback redirect (`http://127.0.0.1:<port>/callback`).

When the CLI cannot open a browser (an SSH session, a sandbox, an agent), use `--no-browser`. The command prints the sign-in URL and exits. Open the URL on any device, then finish the sign-in:

```sh
velixo login --no-browser
velixo login --complete "<code-or-redirected-url>"
```

`--complete` accepts the code, or the full URL that the browser was redirected to. A sign-in with a device code, where you type a short code on another device, is coming. Until it ships, `--complete` takes the redirected URL.

Choose another environment with `--env`:

```sh
velixo login --env nx-dev
```

For jobs that cannot sign in, set `VELIXO_TOKEN`. It holds a refresh token, which `velixo auth token` prints for the current profile. Two limits apply:

- The server rotates a refresh token every time it is used. Do not share one token between jobs that run at the same time: the second job fails after the first one refreshes.
- A refresh token ends with the 30-day session. After that, sign in again.

## Usage

Run `velixo` with no arguments in a terminal to see the welcome screen: the version, whether you are signed in, and the four commands that start the work. In a pipe, a script or an agent session the same bare `velixo` prints the help text instead. `NO_COLOR` or `--no-color` turns the colours off.


```sh
velixo [command] [flags]
velixo [command] --help
```

Every command that calls a Velixo Intelligence tool needs `--intent`, a short sentence that says why you make the call. The server records it. Set `VELIXO_INTENT` to give one intent to a whole script.

### Sign in and check the setup

```sh
velixo login
velixo whoami
velixo status
```

`velixo status` checks the server, your sign-in and your connections. It exits 0 when everything works and 4 when you must sign in.

### List connections

```sh
velixo connections list
velixo connections list --status ok
```

The output is a JSON array on a pipe and a table on a terminal:

```json
[
  { "name": "gab-dev", "type": "acumatica", "status": "ok", "description": "Acumatica 2025 R1, tenant Company" },
  { "name": "skyline", "type": "sage_intacct", "status": "ok", "description": "Skyline Industries" }
]
```

### Build a query, run it with a parameter and pipe the rows into jq

Build the query from a request. Write every value as a `{{Placeholder}}`. Save the plan:

```sh
velixo query build "Outstanding AR balance per customer as of {{As Of Date}}" \
  --connection gab-dev \
  --intent "Find the overdue invoices to chase before month-end" \
  > ar-balance.plan.json
```

Take the query out of the plan and run it. `--param` takes the parameter's display label, the text after `AS` in its `DECLARE` line:

```sh
jq -r '.result.data.queries[0].query' ar-balance.plan.json > ar-balance.vql

velixo query run --query ar-balance.vql \
  --param "As Of Date=2026-09-30" \
  --connection gab-dev \
  --intent "Find the overdue invoices to chase before month-end" \
  | jq '.result.data.rows[] | {customer: .[0], balance: .[1]}'
```

To run every item of a plan, use `--plan ar-balance.plan.json` instead of `--query`. The query returns 250 rows by default. Use `--top` and `--skip` to choose a window, or `--all` to read every window up to `--max-rows` (default 10,000). When `result.data.truncated` is `true`, the ERP matched more rows than the call returned.

### Export CSV

```sh
velixo query run --query ar-balance.vql \
  --param "As Of Date=2026-09-30" \
  --output csv \
  --intent "Export the AR balance for the collections review" \
  > ar-balance.csv
```

The first line holds the column captions. To get a Velixo workbook with the query as a live formula, use `velixo query workbook --query ar-balance.vql --download ar-balance.xlsx`.

### Build and run a screen batch

A screen batch runs commands on an Acumatica screen. Build it once. The batch is a template that you can run again with other values:

```sh
velixo screen build "Create a sales order for customer {{CustomerID}} with the lines I supply" \
  --connection gab-dev \
  --intent "Enter the Q4 order for Acme" \
  > create-order.batch.json
```

Check it first. `--dry-run` sends nothing. It binds the variables on your machine, reports every missing or unknown variable and prints the summary of what the batch would do:

```sh
velixo screen run --batch create-order.batch.json \
  --var CustomerID=ACME --rows lines.json \
  --connection gab-dev --dry-run \
  --intent "Enter the Q4 order for Acme"
```

Run it with `--yes`:

```sh
velixo screen run --batch create-order.batch.json \
  --var CustomerID=ACME --rows lines.json \
  --connection gab-dev --yes \
  --intent "Enter the Q4 order for Acme"
```

Without a terminal and without `--yes`, the command exits with code 2 and does not run. The key that the ERP assigned is in `result.feedback.record`. Running the same batch again within 10 minutes writes again, so the CLI refuses it with `DUPLICATE_RUN` unless you add `--force`.

### Upload a file and attach it

`velixo files upload` stages a file and prints a token that is valid for about an hour:

```sh
velixo files upload invoice-4411.pdf
```

```json
[
  { "fileName": "invoice-4411.pdf", "sizeBytes": 183220, "sha256": "...", "fileToken": "ut_...", "expiresAt": "2026-10-28T16:10:00Z" }
]
```

`screen run --attach` stages the file for you and attaches it to the record that the batch saves. Add a comment after a colon:

```sh
velixo screen run --batch create-bill.batch.json \
  --var VendorID=ACME \
  --attach "invoice-4411.pdf:Vendor invoice" \
  --yes --intent "Enter the vendor bill with its invoice"
```

To fetch the reports and files that a batch produced, add `--download-dir out/`.

### Follow a long task

A build or a run can take minutes. The CLI waits by default and prints progress on stderr. To start a task and collect it later, use `--no-wait`:

```sh
velixo query build "Revenue by customer for {{Fin Period}}" \
  --no-wait --intent "Prepare the revenue report"
# { "taskId": "t_5c1e...", "status": "running" }

velixo task status t_5c1e... --wait
```

`--timeout <seconds>` limits how long the CLI waits. When the time runs out, the CLI exits with code 124 and the task keeps running on the server. Run `velixo task status <taskId> --wait` to continue waiting.

### Import with an Acumatica import scenario

```sh
velixo import scenarios --connection gab-dev --filter "Customers"
velixo import run --connection gab-dev --scenario "Import Customers" --file customers.csv --validate-only
velixo import run --connection gab-dev --scenario "Import Customers" --file customers.csv --yes
```

## Commands

| Group | Commands |
|---|---|
| Sign in and status | `login`, `logout`, `whoami`, `status`, `auth token` |
| Connections | `connections list` |
| Guidance | `guide`, `guide --cli` |
| Queries | `query build`, `query run`, `query workbook` |
| Screen commands (Acumatica) | `screen build`, `screen run` |
| Tasks | `task status` |
| Files | `files upload`, `files request`, `files staged`, `files download` |
| Import scenarios (Acumatica) | `import scenarios`, `import run` |
| Notifications | `notify` |
| Raw access and discovery | `tools list`, `call`, `help --all` |
| Configuration | `config get`, `config set`, `config list`, `config path`, `version` |
| Agent setup (planned) | `agent setup`, which exits 2 with `NOT_IMPLEMENTED` in this version |

[docs/commands.md](docs/commands.md) lists the flags and the output of each command. `velixo [command] --help` shows the same on the command line.

`velixo call <tool>` calls any tool of the MCP server, so a tool that the server adds is available in the CLI on the same day. `velixo agent setup`, which installs the `velixo-cli` skill for your agent, is planned for a later release.

### Shared flags

| Flag | Meaning |
|---|---|
| `--profile` | The profile to use (an environment, an organization and their tokens). |
| `--env` | The environment: `prod` (default), `nx-staging`, `nx-stage-zero`, `nx-dev` or `local`. |
| `--url` | The MCP URL of any other server. |
| `--connection` | The ERP connection. Optional on query and screen commands, required on import commands. |
| `--intent` | Why you make the call. Required on commands that call a working tool. |
| `--output` | `json` (default when stdout is not a terminal), `table` (default on a terminal) or `csv`. |
| `--quiet` | Silence stderr except errors. |
| `--no-color` | Turn colors off. |
| `--non-interactive` | Never prompt. |
| `--yes` | Confirm a write. |
| `--timeout <seconds>` | Stop waiting after this many seconds: one budget for the whole command. |
| `--no-wait` | Print the task id and return. |

### Output

stdout carries the result: one JSON document on a pipe, or a table on a terminal. stderr carries progress, warnings and the confirmation summary. JSON is never shortened. The CLI has no pager. A relative path in any flag is read against the working directory.

On a non-zero exit in JSON mode, stdout holds one error document:

```json
{
  "error": {
    "code": "SCREEN_SESSION_LOST",
    "exitCode": 5,
    "message": "SCREEN_SESSION_LOST: the screen session that held the open record is gone. ...",
    "taskId": "t_8f3a...",
    "nextStep": "Read the record before doing anything else, then start it again from its header."
  }
}
```

`message` is the server's text, unchanged. `nextStep` is the one-line recovery for that code.

## Exit codes

| Code | Meaning | What to do |
|---|---|---|
| 0 | Success. A truncated query result or a warning is still a success: read `result.data.truncated` and `warnings`. | Continue. |
| 1 | Unexpected failure: network, HTTP 5xx, a bug. | Retry once, then report. |
| 2 | Usage error: a bad flag, a missing `--intent`, a confirmation required in non-interactive mode, or a duplicate write without `--force`. | Fix the command line, or ask the user. |
| 3 | The builder refused: the plan is an `ERROR:` text that asks for a decision or a value. | Put the question to the user in plain words and build again. |
| 4 | Not signed in, the session ended, or the Velixo gate denied access (no `mcpServerEnabled`, no credits, no account). | Run `velixo login`, or relay the reason. |
| 5 | The server refused the call, with a code: `ATTACHMENT_*`, `SCREEN_*`, `IMPORT_*`, `REPORT_*` or `BC_*`. | Read `error.code`. |
| 6 | The task failed in the ERP: `status: "failed"`, or an HTTP 400 from the ERP on a query. | Read `error.detail`. For a query error, build the query again and quote the error. |
| 124 | `--timeout` ran out while waiting. The task still runs. | `velixo task status <taskId> --wait` |
| 130 | Interrupted with Ctrl-C. A running task keeps running on the server. | |

The codes you meet most often:

| `error.code` | Exit | Meaning |
|---|---|---|
| `INTENT_REQUIRED` | 2 | No `--intent` on a command that calls a working tool. |
| `CONFIRMATION_REQUIRED` | 2 | A write without `--yes` and without a terminal. Nothing ran. |
| `CONFIRMATION_DECLINED` | 2 | The answer at the terminal prompt was no. Nothing ran. |
| `DUPLICATE_RUN` | 2 | The same screen write ran in the last 10 minutes. |
| `BUILDER_REFUSAL` | 3 | The builder asks a question. `message` holds it verbatim. |
| `NOT_SIGNED_IN`, `SESSION_ENDED`, `ACCESS_DENIED` | 4 | Sign in again, or relay the reason. |
| `ATTACHMENT_*`, `SCREEN_*`, `IMPORT_*`, `REPORT_*`, `BC_*` | 5 | The server's code, whether the tool refused the call or the task failed with it. |
| `TASK_FAILED`, `ERP_ACTION_REFUSED` | 6 | The ERP failed the task, or refused an action because of the record's state. |

[docs/commands.md](docs/commands.md#error-codes) lists every code.

## Environment variables

| Variable | Meaning |
|---|---|
| `VELIXO_PROFILE` | The profile to use. |
| `VELIXO_ENV` | The environment to use. |
| `VELIXO_MCP_URL` | The MCP URL to use. |
| `VELIXO_CONNECTION` | The default ERP connection. |
| `VELIXO_INTENT` | The intent for every call of a script. |
| `VELIXO_TOKEN` | A refresh token for jobs that cannot sign in. |
| `VELIXO_OUTPUT` | The default output format. |
| `VELIXO_CONFIG_DIR` | Moves the configuration folder. |
| `VELIXO_CLIENT_ID` | The OAuth client id a `VELIXO_TOKEN` was issued to, on a machine that never signed in itself. |
| `VELIXO_INSTALL_DIR` | The install folder of `install.sh`. |
| `NO_COLOR` | Turns colors off. |
| `XDG_CONFIG_HOME` | Moves the configuration folder on Linux and macOS. |

The CLI reads settings in this order, highest first: flag, environment variable, `.velixo.json`, profile, built-in default.

## Configuration files

| Location | Holds |
|---|---|
| `$XDG_CONFIG_HOME/velixo/config.json`. Linux and macOS: `~/.config/velixo/config.json`. Windows: `%APPDATA%\velixo\config.json`. | The default profile, the default output and each profile's environment, URL and connection. |
| `credentials.json` in the configuration folder, mode 0600 | The access and refresh tokens of each profile. |
| `state/<profile>.json` in the configuration folder | The MCP session id, the last continuation and the recent write fingerprints. |
| `.velixo.json` in the working directory or a parent | Project defaults: the profile and the connection. Never credentials. |

A profile is one environment, one organization and the tokens for it. `velixo login --env nx-dev` creates the profile `nx-dev`. `--profile acme-prod` names a profile, for example when you work for several customers on `prod`. A session belongs to one organization: to switch, run `velixo logout` and `velixo login`, or use a second profile. Run `velixo config path` to print the folder.

## For AI agents

If you are an AI agent, read this section and follow it.

1. Run `velixo guide --cli` for the command mapping, the exit codes and the rules for writes.
2. Run `velixo guide` for the server's tool rules. They live on the server, so they are always current. `velixo guide <tool-or-command>` prints one section, for example `velixo guide screen run`.
3. Run `velixo help --all --output json` to read the whole command tree with its flags in one call.
4. Pass `--intent` on every command that calls a working tool.
5. Every write needs `--yes`. Describe the change to the user in business words and wait for their yes before you add `--yes`. The CLI never assumes consent.
6. Read the exit code. On a non-zero code, read `error.code` and `error.nextStep` in the JSON on stdout.

When the CLI detects an agent (`CLAUDECODE`, `CURSOR_AGENT`, `CODEX_SANDBOX`, `CODEX_THREAD_ID`, `GEMINI_CLI`, `CLINE_ACTIVE`, `OPENCODE`, `KIRO_SESSION_ID` or `AI_AGENT`), it turns on non-interactive mode: it never prompts and never opens a browser.

## Telemetry

The CLI sends no telemetry of its own. The Velixo server records every call with the caller, the name of the agent behind the call and your `--intent` text. The CLI detects the agent from the environment variables listed above and sends its name with each call.


## Documentation

- [docs/commands.md](docs/commands.md): every command, flag and output.
- [docs/agent-guide.md](docs/agent-guide.md): the guide that `velixo guide --cli` prints.
- [CHANGELOG.md](CHANGELOG.md): what changed in each version.

## Feedback

Contact Velixo support with the output of `velixo version` and `velixo status`. Include the `taskId` from the error document when you report a failed call.

## Contributing

The source of the CLI is proprietary. This repository does not accept outside code contributions.

## License

Copyright (c) 2026 Velixo Solutions Inc. All rights reserved. The CLI is proprietary software, licensed to Velixo users only. See [LICENSE](LICENSE).
