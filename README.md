<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://secure.velixo.com/logos/logo-full-gradient-white.svg">
    <img src="https://secure.velixo.com/logos/logo-full-gradient-navy.svg" alt="Velixo" width="240">
  </picture>
</p>

# Velixo CLI

The Velixo CLI brings Velixo to the command line, so AI coding agents, scripts and people at a terminal can query an ERP, run ERP commands, move files and run imports from a shell.

[![Latest release](https://img.shields.io/github/v/release/VelixoSolutions/velixo-cli)](https://github.com/VelixoSolutions/velixo-cli/releases/latest)
[![CI](https://github.com/VelixoSolutions/velixo-cli/actions/workflows/ci.yml/badge.svg)](https://github.com/VelixoSolutions/velixo-cli/actions/workflows/ci.yml)

The CLI is one binary named `velixo` for Linux, macOS and Windows. It needs no runtime on your machine. It uses the same API and the same tools as the Velixo MCP, which Claude, ChatGPT and Teams use, so the answers and the rules are the same wherever you ask.

The CLI is for agents that have a shell (Claude Code, Codex, Cursor, Gemini CLI), for CI jobs and for people who prefer a terminal. Hosted chat apps that cannot run a binary keep using Velixo's connector for them.

## Before you start

You need three things:

1. A Velixo account on the Velixo portal.
2. Access to Velixo's AI tools, turned on for you by a Velixo administrator.
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
velixo login --env nx-staging
```

For jobs that cannot sign in, set `VELIXO_TOKEN`. It holds a refresh token, which `velixo auth token` prints for the current profile. Two limits apply:

- The server replaces a refresh token every time it is used, and refuses one that was already used: a second job that presents the same token ends the session for both. Give each job its own token, and never run two jobs on one token at once.
- A refresh token ends with the 30-day session. After that, sign in again.

## Usage

Run `velixo` with no arguments in a terminal to see the welcome screen: the version, whether you are signed in, and every command, grouped, with a one-line summary. In a pipe, a script or an agent session the same bare `velixo` prints the help text instead. `NO_COLOR` or `--no-color` turns the colours off.


```sh
velixo [command] [flags]
velixo [command] --help
```

Every command that calls a Velixo Intelligence tool sends an intent, a short sentence that says why you make the call. The server records it, so that an administrator can see why each call was made. Give it with `--intent`, or set `VELIXO_INTENT` to give one intent to a whole script. Without either, the CLI sends the command name, for example `Run from the Velixo CLI: velixo query run`. It never sends the values of your arguments there, because they can be customer data.

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
  { "name": "gab-dev", "type": "acumatica", "status": "ok", "description": "Acumatica 2025 R1, tenant Company", "default": true },
  { "name": "skyline", "type": "sage_intacct", "status": "ok", "description": "Skyline Industries" }
]
```

### Choosing a connection

A command that works in an ERP takes the connection from the first of these that names one:

1. `--connection NAME`.
2. `VELIXO_CONNECTION`.
3. `connection` in `.velixo.json`.
4. The profile's default connection, which `velixo connections switch` sets.
5. The server's own choice. The server chooses only when one connection fits. When several fit, the command fails, and the error names `velixo connections switch NAME`.

```sh
velixo connections switch            # pick from the list with the arrow keys, Enter to select
velixo connections switch gab-dev    # or name it, in a script or an agent
velixo connections switch --clear
```

`velixo connections switch` writes the current profile's `connection` in `config.json`, as `velixo config set profiles.<name>.connection` does. It reads the list from the server, so it needs a sign-in, and it refuses a name that is not on the list. Without a terminal it needs the name. `--connection` still overrides the default on any command. `velixo connections list` marks the connection that this order chooses: `"default": true` in JSON, and `(default)` after its name in a table.

### Build a query, run it with a parameter and pipe the rows into jq

Build the query from a request. Write every value as a `{{Placeholder}}`. The build keeps the plan in a numbered list and prints its number; `--name` gives it a name too, and `--save FILE.vql` also writes the query text to a file:

```sh
velixo query build "Outstanding AR balance per customer as of {{AsOfDate}}" \
  --connection gab-dev --name ar-balance --save ar-balance.vql \
  --intent "Find the overdue invoices to chase before month-end"
```

Run it by its name, or from the file. `--param` takes the parameter's display label, the text after `AS` in its `DECLARE` line. At a terminal, a value you leave out is asked for:

```sh
velixo query run ar-balance --param "As Of Date=2026-09-30"

velixo query run --query ar-balance.vql \
  --param "As Of Date=2026-09-30" \
  --connection gab-dev \
  --intent "Find the overdue invoices to chase before month-end" \
  | jq '.result.data.rows[] | {customer: .[0], balance: .[1]}'
```

A plan of several queries runs by its number or name, or from a file saved with `--save FILE.json` and `--plan`. The query returns 250 rows by default. Use `--top` and `--skip` to choose a window, or `--all` to read every window up to `--max-rows` (default 10,000). When `result.data.truncated` is `true`, the ERP matched more rows than the call returned.

### Export CSV

```sh
velixo query run --query ar-balance.vql \
  --param "As Of Date=2026-09-30" \
  --output csv \
  --intent "Export the AR balance for the collections review" \
  > ar-balance.csv
```

The first line holds the column captions. To get a Velixo workbook with the query as a live formula, use `velixo query workbook --query ar-balance.vql --download ar-balance.xlsx`.

### Build and run a batch of ERP commands

A batch reads or changes records, or runs a report, through the ERP's screens or objects. Build it once; the build keeps it in a numbered list. The batch is a template that you can run again with other values:

```sh
velixo command build "Create a sales order for customer {{CustomerID}} with the lines I supply" \
  --connection gab-dev --name create-order \
  --intent "Enter the Q4 order for Acme"
```

Check it first. `--dry-run` sends nothing. It binds the variables on your machine, reports every missing or unknown variable and prints the summary of what the batch would do:

```sh
velixo command run create-order \
  --param CustomerID=ACME --rows lines.json --dry-run \
  --intent "Enter the Q4 order for Acme"
```

Run it with `--yes`:

```sh
velixo command run create-order \
  --param CustomerID=ACME --rows lines.json --yes \
  --intent "Enter the Q4 order for Acme"
```

Without a terminal and without `--yes`, the command exits with code 2 and does not run. The key of the saved record, including an ID the ERP assigned, is in `result.feedback.savedKey`. Running the same batch again within 10 minutes writes again, so the CLI refuses it with `DUPLICATE_RUN` unless you add `--force`.

### Upload a file and attach it

`command run --attach` uploads a file from your disk and attaches it to the record that the batch saves. Give one `--attach` per file, with its path relative to the current folder or absolute. To store a comment with a file on the record, put `--attach-comment` right after that file's `--attach`:

```sh
velixo command run create-bill --param VendorID=ACME \
  --attach bills/invoice-4411.pdf --attach-comment "Vendor invoice 4411" \
  --attach bills/delivery-note.pdf \
  --attach bills/po-8812.pdf --attach-comment "Customer PO" \
  --yes --intent "Enter the vendor bill with its documents"
```

All three files go on the one bill this run saves: `invoice-4411.pdf` and `po-8812.pdf` with their comments, `delivery-note.pdf` without one. A run takes up to 5 files, 5 MB each and 20 MB in all. Files for another record take another run, with a batch that opens that record.

To upload the file ahead of the run, or from another machine, stage it and pass its token:

```sh
velixo files stage invoice-4411.pdf                   # uploads it and prints the fileToken
velixo files stage invoice-4411.pdf --size 48213      # or prints an upload URL and the curl command that sends it
velixo command run create-bill --param VendorID=ACME --attach-token TOKEN --attach-comment "Vendor invoice 4411" --yes
```

To fetch the reports and files that a batch produced, add `--download-dir out/`. A download link needs no sign-in, so `curl -o` works too.

### Follow a long task

A build or a run can take minutes. The CLI waits by default and prints progress on stderr. To start a task and collect it later, use `--no-wait`:

```sh
velixo query build "Revenue by customer for {{FinPeriod}}" \
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
| Connections | `connections list`, `connections switch` |
| Guidance | `guide` |
| Queries | `query build`, `query run`, `query workbook`, `query list`, `query delete` |
| ERP commands | `command build`, `command run`, `command list`, `command delete` |
| Tasks | `task status` |
| Files | `files stage`, `files download` |
| Import scenarios (Acumatica) | `import scenarios`, `import run` |
| Notifications | `notify` |
| Discovery | `tools list`, `tools guidance`, `help --all` |
| Configuration | `config get`, `config set`, `config list`, `config path`, `version` |

[docs/commands.md](docs/commands.md) lists the flags and the output of each command. `velixo [command] --help` shows the same on the command line.


### Shared flags

| Flag | Meaning |
|---|---|
| `--profile` | The profile to use (an environment, an organization and their tokens). |
| `--env` | The environment: `prod` (default), `nx-staging`, `nx-stage-zero`, `nx-dev` or `local`. |
| `--url` | The address of any other Velixo server, such as a preview environment: `--url https://feature-x.velixo.com`. |
| `--connection` | The ERP connection. Optional on queries and ERP commands; an import needs one you state. |
| `--intent` | Why you make the call. The server records it for an administrator. Optional: without it, the CLI sends the command name. |
| `--output` | `json` (default when stdout is not a terminal or an AI agent is detected), `table` (default otherwise) or `csv`. |
| `--quiet` | Print no progress on stderr; warnings and errors still print. |
| `--no-color` | Turn colors off. |
| `--non-interactive` | Never prompt. |
| `--yes` | Confirm a write. |
| `--timeout <seconds>` | Stop waiting after this many seconds: one budget for the whole command. |
| `--no-wait` | Print the task id and return. |

### Output

stdout carries the result: one JSON document on a pipe, or a table on a terminal. stderr carries progress, warnings and the confirmation summary. JSON is never shortened. At a terminal, query results open in a pager; a pipe, a script or an agent gets one window and never a pager. A relative path in any flag is read against the working directory.

On a non-zero exit in JSON mode, stdout holds one error document:

```json
{
  "error": {
    "code": "SCREEN_SESSION_LOST",
    "exitCode": 5,
    "message": "SCREEN_SESSION_LOST: the continuation expired at 2026-10-28T15:22:00Z. ...",
    "taskId": "t_8f3a...",
    "nextStep": "Start the record again from its header. If the header was saved, load it by key in a new header."
  }
}
```

`message` is the server's text, unchanged. `nextStep` is the one-line recovery for that code.

## Exit codes

| Code | Meaning | What to do |
|---|---|---|
| 0 | Success. A truncated query result or a warning is still a success: read `result.data.truncated` and `warnings`. | Continue. |
| 1 | Unexpected failure: network, HTTP 5xx, a bug. | A read or a build: retry once, then report. A write (`command run`, `import run`, `notify`): never retry, it may have run; read the record, then report. |
| 2 | Usage error: a bad flag, a confirmation required in non-interactive mode, or a duplicate write without `--force`. | Fix the command line, or ask the user. |
| 3 | The builder refused: the plan is an `ERROR:` text that asks for a decision or a value. | Put the question to the user in plain words and build again. |
| 4 | Not signed in, the session ended, or Velixo denied access (no access to the AI tools, no credits, no account). | Run `velixo login`, or relay the reason. |
| 5 | The server refused the call, with a code: `ATTACHMENT_*`, `SCREEN_*`, `IMPORT_*`, `REPORT_*` or `BC_*`. | Read `error.code`. |
| 6 | The task failed in the ERP: `status: "failed"`, or an HTTP 400 from the ERP on a query. | Read `error.detail`. For a query error, build the query again and quote the error. |
| 124 | `--timeout` ran out while waiting. The task still runs. | `velixo task status <taskId> --wait` |
| 130 | Interrupted with Ctrl-C. A running task keeps running on the server. | |

The codes you meet most often:

| `error.code` | Exit | Meaning |
|---|---|---|
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
| `VELIXO_URL` | The address of the Velixo server to use, as `--url` gives it. |
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
| `library/<profile>.json` in the configuration folder | The numbered list of built queries and batches. `logout` keeps it. |
| `state/<profile>.json` in the configuration folder | The server session, the last continuation and the recent write fingerprints. |
| `.velixo.json` in the working directory or a parent | Project defaults: the profile and the connection. Never credentials. |

A profile is one environment, one organization and the tokens for it. `velixo login --env nx-dev` creates the profile `nx-dev`. `--profile acme-prod` names a profile, for example when you work for several customers on `prod`. A session belongs to one organization: to switch, run `velixo logout` and `velixo login`, or use a second profile. Run `velixo config path` to print the folder.

## For AI agents

If you are an AI agent, read this section and follow it.

1. Run `velixo guide` for the workflow, the rules for writes and the exit codes.
2. Run `velixo guide <command>` before you use a command for the first time, for example `velixo guide command run`. `velixo guide command` prints every page of the group.
3. Run `velixo help --all --output json` to read the whole command tree with its flags in one call.
4. Pass `--intent` with the user's goal on every command that calls a working tool. It is optional and defaults to the command name, but an administrator reads it to see why each call was made.
5. Every change to the ERP needs `--yes`. Check it first (`--dry-run`, `--validate-only`), describe the change to the user in business words, and wait for their yes before you add `--yes`. The CLI never assumes consent. `notify` sends at once, with no check: send only what the user asked for.
6. Read the exit code. On a non-zero code, read `error.code` and `error.nextStep` in the JSON on stdout.

When the CLI detects an agent (`CLAUDECODE`, `CURSOR_AGENT`, `CODEX_SANDBOX`, `CODEX_THREAD_ID`, `GEMINI_CLI`, `CLINE_ACTIVE`, `OPENCODE`, `KIRO_SESSION_ID` or `AI_AGENT`), it turns on non-interactive mode: it never prompts and never opens a browser.

## Telemetry

The CLI sends no telemetry of its own. The Velixo server records every call with the caller, the name of the agent behind the call and your `--intent` text, or the command name when you gave none. The CLI detects the agent from the environment variables listed above and sends its name with each call.


## Documentation

- [docs/commands.md](docs/commands.md): every command, flag and output.
- [docs/guide/](docs/guide/get-started.md): the pages that `velixo guide` prints.
- [CHANGELOG.md](CHANGELOG.md): what changed in each version.

## Feedback

Contact Velixo support with the output of `velixo version` and `velixo status`. Include the `taskId` from the error document when you report a failed call.

## Contributing

The source of the CLI is proprietary. This repository does not accept outside code contributions.

## License

Copyright (c) 2026 Velixo Solutions Inc. All rights reserved. The CLI is proprietary software, licensed to Velixo users only. See [LICENSE](LICENSE).
