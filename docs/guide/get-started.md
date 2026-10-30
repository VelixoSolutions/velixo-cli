<!-- commands: -->
# Velixo CLI: get started

The `velixo` command answers questions about a connected ERP (Acumatica, Sage Intacct, Business Central) and changes records in it. You describe what you want in words. Velixo's builders turn the words into a validated query or a batch of screen commands. You run that, and the CLI waits for the result.

This page is the workflow and the rules that hold for every command. Each command has its own page with its flags, examples and errors: `velixo guide query build`, `velixo guide command run`, and so on. `velixo guide command` prints every page of a group.

## First steps

1. Run `velixo status`. Exit 0 means you can work. Exit 4 means you must sign in: run `velixo login`. Without a browser, run `velixo login --no-browser`, open the URL it prints, then run `velixo login --complete <code-or-url>`.
2. Run `velixo connections list` to see the ERP connections. When several exist and the user did not say which, ask. Never guess. Pass `--connection NAME` on a command, or set the default with `velixo connections switch`.
3. Read the page of a command before you use it for the first time: `velixo guide query build`.

## The core loop

1. **Pick the command.**
    - A question about data ("show", "list", "total", "compare"): `velixo query build`, then `velixo query run`.
    - A request for the query text itself: `velixo query build`, and hand over what it printed, unedited.
    - Reading a specific screen, creating or updating a record, running an action, or running a report: `velixo command build`, then `velixo command run`.
    - The files attached to a record: the same pair, with a request to list or download the attachments.
    - A file to attach to a record: `velixo command run --attach PATH`.
    - A file to load through an import scenario: `velixo import scenarios`, then `velixo import run`.
    - When a query and a screen read could both serve, use the query for tabular data, and a screen read only for a screen the user named.
2. **Clarify before you build.** When the request lacks a filter, the entity, the period or the grouping, ask for every missing input in one message, then build. Which screen or report to use is not a missing input: the builder finds it.
3. **Build.** stdout holds the plan or the batch, with `libraryNumber` at the top of the JSON: the build is kept in the profile's numbered list (and named `libraryName` with `--name`). Add `--save FILE` when you will refine a query, because `--base-query` takes a file.
4. **Run** it by its number, `velixo query run 3` or `velixo command run 3`, with every user value as `--param`. Nothing is asked of you: a query with a value missing warns on stderr and matches nothing, and a batch with one missing exits 5 `SCREEN_VARIABLE_MISSING`. A list entry runs on the connection it was built on. A plan or a batch run from a file does not remember its connection: pass the same `--connection` you built with.
5. **Report** the result (see the end of this page).

## Rules

Break one of these and the failure is usually silent: a plausible wrong figure, a second record nobody asked for, or a change nobody approved.

- **No write without the user's yes.** Check with `--dry-run` or `--validate-only`, describe the change in business words, wait for yes, then run with `--yes`. A permission prompt from your own host is not a yes.
- **Never invent an identifier or a connection.** A customer, vendor, branch, record key or connection the user did not give is a question for the user.
- **The request carries field names, not values.** Write every value the user gave as a `{{Placeholder}}`: "the AR balance for customer `{{CustomerID}}`", never "for ABARTENDE". Pass the value when you run: `--param "Customer ID=ABARTENDE"` for a query, by its label, or `--param CustomerID=ABARTENDE` for a batch, by its placeholder. What you choose yourself (a ledger, a status, released only) is not a user value: write it in words.
- **The builder is the only author.** Never write or edit a query or a batch by hand, not even a one-word fix. A field that exists but means something else returns wrong data with no error.
- **A change of shape rebuilds.** Another filter, a sort, a grouping, other columns: build again with the full refined request. For a query, pass the query you hold with `--base-query FILE`, so the figures you are not changing stay the same.
- **A different value re-runs.** The same question for another customer or period is the file you already hold, run again with another `--param`. Rebuilding can compute the figure a second way and give two answers that disagree.
- **Running a batch again writes again.** Running a query again is free. Running "the same invoice for BCORP" creates a second invoice. Every run that changes data needs its own confirmation.

## When a builder refuses

`query build` and `command build` exit 3 when the request cannot be built as asked: a value is missing, or the builder needs a decision, such as which grouping axis to use for a breakdown. Put the question to the user in plain words, get the answer, and build again. Never guess a value or an axis to get past it. The command page says which refusals you can settle yourself.

## Rules for every command

- Pass `--intent "<the user's goal>"` on every command that works in the ERP. It is optional: without it the CLI sends the command name, such as `Run from the Velixo CLI: velixo query run`. Pass the goal anyway, because an administrator reads it to see why each call was made. `VELIXO_INTENT` sets it once for a script.
- stdout holds the result: one JSON document when stdout is not a terminal or an agent is detected, a table otherwise. stderr holds progress and warnings. Parse stdout only.
- On failure, JSON mode prints `{ "error": { "code", "exitCode", "message", "taskId", "detail", "nextStep" } }`. `message` is the server's text, `detail` the ERP's own message when there is one, and `nextStep` the recovery. Read them all.
- `--compact` prints JSON on one line and `--quiet` silences progress on stderr: both save context. Warnings still print on stderr with `--quiet`, and you must read them: a `--param` label that matches nothing is only a warning.
- Never show the user a raw `ERROR:` text or an error code. Say the problem in plain words.
- The CLI does not prompt when stdin is not a terminal or an agent is detected, and it never opens a browser then.
- A long task makes the CLI wait, with progress on stderr. `--no-wait` starts it and returns; `velixo task status TASKID --wait` collects it.

## Writes

`velixo command run` and `velixo import run` can change the ERP. For each:

1. Check first: `command run --dry-run`, or `import run --validate-only`.
2. Tell the user in business words what will change: which record, which values, which connection. Not the batch, not the command line.
3. Wait for the user's yes.
4. Run it with `--yes`.

`velixo notify` sends at once, with no check and no question: send only what the user asked for, to whom they asked.

Never retry a write or a notification that failed: it may have gone through. Read the record first.

## Exit codes

| Code | Meaning | What to do |
|---|---|---|
| 0 | Success. A truncated result or a warning is still a success. | Read `truncated` and `warnings`, then continue. |
| 1 | Unexpected failure: network, HTTP 5xx, a bug. | A read or a build: retry once, then report. A write (`command run`, `import run`, `notify`): never retry, it may have run. Read the record, then report. |
| 2 `CONFIRMATION_REQUIRED` | A write without `--yes`. Nothing ran. | Ask the user. Never add `--yes` without their yes. |
| 2 `DUPLICATE_RUN` | The same batch ran in the last 10 minutes. | It already ran: read the record. `--force` only when the user wants a second one. |
| 2 `USAGE` and others | A bad flag or value. | Fix the command line. |
| 3 | The builder refused and asks for a decision or a value. | Ask the user in plain words, then build again. |
| 4 | Not signed in, the session ended, or Velixo denied access (no access to the AI tools, no credits, no account). | `velixo login`, or tell the user the reason. |
| 5 | The server refused the call, with a code such as `ATTACHMENT_*`, `SCREEN_*`, `IMPORT_*`, `REPORT_*` or `BC_*`. | Read `error.code` and `nextStep`; they say whether the ERP was touched. |
| 6 | The task failed in the ERP. | Read `error.detail`. The command page says what to do. |
| 124 | `--timeout` ran out. The task still runs. | `velixo task status TASKID --wait` |
| 130 | Interrupted. A task the command started keeps running. | `velixo task status TASKID` |

## Tell the user what you did

- Report in business words: what you asked the ERP, the figures or the record key, and anything truncated, skipped or refused. Quote the warnings you saw.
- Show a table of up to about 250 rows. Beyond that, say "Showing N of M rows" and offer the rest; the query pages say how.
- A report or a file arrives as a link, and the link is what you deliver. Never describe a file you only hold a link to. Links expire in about an hour.
- Never report a write as done until the output shows it, with the key the ERP assigned.

## Pages

| Page | Commands |
|---|---|
| `velixo guide sign-in` | `login`, `logout`, `whoami`, `status`, `auth token` |
| `velixo guide connections` | `connections list`, `connections switch` |
| `velixo guide query build` | Build a query from a question |
| `velixo guide query run` | Run a plan, a query or a saved inquiry; rows and windows |
| `velixo guide query workbook` | A Velixo workbook from a query |
| `velixo guide command build` | Build a batch of screen commands |
| `velixo guide command run` | Run a batch: reads, reports, writes, attachments, continuations |
| `velixo guide task status` | Long-running tasks |
| `velixo guide library` | `query list`, `query delete`, `command list`, `command delete`: the built plans and batches, by number |
| `velixo guide files` | Files you attach or import, `files stage` and `files download` |
| `velixo guide import` | `import scenarios`, `import run` |
| `velixo guide notify` | Notifications to the portal, email, Teams or Slack |
| `velixo guide apps` | Calling the CLI from an app: saved files, JSON answers, error codes, parallel calls |
| `velixo guide setup` | `guide`, `config`, `tools`, `help`, `version` |

For a server error code no page lists, `velixo tools guidance TOOL` prints the server's own rules for that tool. They are written for other clients: where names or flags differ, this guide is right.
