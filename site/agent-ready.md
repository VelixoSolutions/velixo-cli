# Velixo CLI for AI agents

This page is for an AI agent with a shell: Claude Code, Codex, Cursor, Gemini CLI and the like. Read it once, then act on it.

The Velixo CLI is one binary, `velixo`, for Linux, macOS and Windows. It answers questions about a connected ERP (Acumatica, Sage Intacct, Business Central) and changes records in it. You describe what you want in words; Velixo's builders turn the words into a validated query or a batch of ERP commands; you run that, and the CLI waits for the result.

- Repository and full documentation: https://github.com/VelixoSolutions/velixo-cli
- Every command, flag and output: https://raw.githubusercontent.com/VelixoSolutions/velixo-cli/main/docs/commands.md

## 1. Decide whether the CLI is the right tool

| | Velixo CLI | Velixo connector (MCP) |
|---|---|---|
| Built for | Agents with a shell, scripts, CI jobs, apps | Hosted chat apps: Claude, ChatGPT, Teams |
| Runs | A binary on the machine | Inside the chat app |
| Server and rules | Same API, same tools, same rules | Same API, same tools, same rules |

If you can run shell commands, use the CLI. If the human works in a hosted chat app that cannot run a binary, tell them to add Velixo's connector there instead, and stop.

The human needs three things, which only they or their Velixo administrator can provide:

1. A Velixo account on the Velixo portal.
2. Access to Velixo's AI tools, turned on by a Velixo administrator.
3. Velixo Intelligence credits.

Without all three, commands that call the server exit with code 4 and print the reason. Relay the reason; do not work around it.

## 2. Install and sign in

Check first: if `velixo version` works, skip the install.

```sh
# macOS, Linux
curl -fsSL https://cli.velixo.com/install.sh | sh     # installs to ~/.local/bin
brew tap velixosolutions/velixo-cli https://github.com/VelixoSolutions/velixo-cli && brew install velixo   # or Homebrew
npm install -g @velixo/cli                            # or npm, on any OS
```

```powershell
# Windows
winget install Velixo.CLI
irm https://cli.velixo.com/install.ps1 | iex          # or the install script
```

Then check the setup:

```sh
velixo status      # exit 0: ready. exit 4: sign in.
```

To sign in, the human approves in a browser. When the CLI detects an agent, it never opens a browser and never prompts, so the sign-in takes two steps:

```sh
velixo login --no-browser     # prints a URL and a short code, then exits
```

Show the human the URL and the code, and ask them to open the URL on any device, enter the code and sign in. Then run the command it printed as `nextStep`, which waits for them:

```sh
velixo login --complete
```

The code expires after 10 minutes. Never try to sign in for the human.

If `VELIXO_TOKEN` is set, skip the sign-in: the job already has a token.

Then list the ERP connections:

```sh
velixo connections list
```

When several connections exist and the human did not say which, ask. Never guess a connection. Pass `--connection NAME` on each command, or set a default with `velixo connections switch NAME`.

## 3. Learn the CLI from the CLI

The CLI carries its own guide, written for agents. Read it before your first command:

```sh
velixo guide                         # the workflow, the rules for writes and the exit codes
velixo guide query build             # the page of one command, before you use it the first time
velixo help --all --output json      # the whole command tree with its flags, in one call
```

## 4. Read data

A question about data ("show", "list", "total", "compare") is a query: build it, then run it.

```sh
velixo query build "Outstanding AR balance per customer as of {{AsOfDate}}" \
  --connection gab-dev --name ar-balance --save ar-balance.vql \
  --intent "Find the overdue invoices to chase before month-end"

velixo query run ar-balance --param "As Of Date=2026-09-30" \
  --intent "Find the overdue invoices to chase before month-end"
```

- Write every value the human gave as a `{{Placeholder}}` in the request, and pass the value with `--param` when you run. Never put the value in the request.
- The builder is the only author. Never write or edit a query by hand, not even a one-word fix.
- Another filter, sort, grouping or set of columns: build again, passing the query you hold with `--base-query FILE`.
- The same question for another customer or period: run the same query again with another `--param`. Do not rebuild.
- `--output csv` exports. `velixo query workbook` returns an Excel workbook with the query as a live formula.

## 5. Change data

Reading a specific screen, creating or updating a record, running an action or a report is a batch of ERP commands: build it, check it, then run it.

```sh
velixo command build "Create a sales order for customer {{CustomerID}} with the lines I supply" \
  --connection gab-dev --name create-order --intent "Enter the Q4 order for Acme"

velixo command run create-order --param CustomerID=ACME --rows lines.json --dry-run \
  --intent "Enter the Q4 order for Acme"
```

Every write follows these four steps. There is no exception.

1. Check it: `command run --dry-run`, or `import run --validate-only`. Nothing is sent.
2. Tell the human in business words what will change: which record, which values, which connection.
3. Wait for their yes. A permission prompt from your own host is not a yes.
4. Run the same command with `--yes` in place of `--dry-run`.

Without `--yes`, a write exits with code 2 `CONFIRMATION_REQUIRED` and nothing runs. Running a batch again writes again: a second record, or the same change twice. Never retry a write that failed: read the record first. `velixo notify` sends at once, with no check: send only what the human asked for.

To attach files to the record a batch saves, add `--attach PATH` (up to 5 files, 5 MB each). Acumatica import scenarios use `velixo import scenarios` and `velixo import run`.

## 6. Read the exit code

stdout holds one JSON document; stderr holds progress and warnings. Parse stdout only, and read the warnings. On a non-zero exit, stdout holds `{ "error": { "code", "exitCode", "message", "taskId", "detail", "nextStep" } }`.

| Exit | Meaning | Do |
|---|---|---|
| 0 | Success. A truncated result or a warning is still a success. | Read `truncated` and `warnings`. |
| 1 | Network, HTTP 5xx, a bug. | A read: retry once. A write: never retry; read the record. |
| 2 | Bad flag, or a write without `--yes`. | Fix the command line, or ask the human. |
| 3 | The builder asks a question. | Put it to the human in plain words, then build again. Never guess past it. |
| 4 | Not signed in, or access denied. | `velixo login --no-browser`, or relay the reason. |
| 5 | The server refused, with a code. | Read `error.code` and `error.nextStep`. |
| 6 | The task failed in the ERP. | Read `error.detail`. |
| 124 | `--timeout` ran out; the task still runs. | `velixo task status TASKID --wait` |

Never show the human a raw error code. Say the problem in plain words.

## 7. Build an app on the CLI

An app in any language can use the CLI as its ERP back end: build the queries and batches once, save them as files with `--save`, and have the app run them with `--param` values and parse the JSON. Run `velixo guide apps` for saved files, JSON answers, error codes and parallel calls, and see "Running in a container" in the README for a server deployment.

Two sample apps, each with the prompt that built it:

- Inventory checker (Node.js, Acumatica): https://github.com/VelixoSolutions/velixo-cli/tree/main/samples/inventorychecker
- Expense receipts by text message (Python, Acumatica and Sage Intacct): https://github.com/VelixoSolutions/velixo-cli/tree/main/samples/expensereceipts

## Always

- Pass `--intent "<the human's goal>"` on every command that works in the ERP. An administrator reads it to see why each call was made. `VELIXO_INTENT` sets it once for a script.
- Never invent a customer, vendor, record key or connection. Ask.
- Report in business words: the figures, the record key the ERP assigned, and anything truncated, skipped or refused. Never report a write as done until the output shows its key.

## Read next

- The README: https://raw.githubusercontent.com/VelixoSolutions/velixo-cli/main/README.md
- Commands, flags and outputs: https://raw.githubusercontent.com/VelixoSolutions/velixo-cli/main/docs/commands.md
- The guide pages `velixo guide` prints: https://github.com/VelixoSolutions/velixo-cli/tree/main/docs/guide
- What changed in each version: https://raw.githubusercontent.com/VelixoSolutions/velixo-cli/main/CHANGELOG.md

## Send feedback

When the CLI, the server or this documentation behaves wrongly, tell the human, and suggest they contact Velixo support with the output of `velixo version` and `velixo status`, and the `taskId` from the error document. Never include tokens or the contents of `credentials.json`.

### Tell your agent

Paste this into `AGENTS.md` or `CLAUDE.md` in a project that works with an ERP through Velixo:

```text
This project uses the Velixo CLI (`velixo`) to read and change ERP data.
Before the first velixo command in a session, read https://cli.velixo.com/agent-ready.md
and run `velixo guide`. Never run a write with --yes until I have said yes to the change.
```
