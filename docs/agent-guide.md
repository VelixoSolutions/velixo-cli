# Velixo CLI: guide for AI agents

You are an AI agent with a shell. This guide tells you how to use the `velixo` command. It covers the command that matches each tool of the Velixo MCP server, the exit codes, the rule for writes and the rule for continuations.

This guide holds only what is specific to the CLI. The rules for each tool (how to word a query request, when to ask the user, what each error code means) live on the server. Read them with `velixo guide`. Do not rely on memory for them.

## First steps

1. Run `velixo status`. Exit 0 means you can work. Exit 4 means the user must sign in: run `velixo login --no-browser`, show the user the URL it prints, and run `velixo login --complete <code-or-url>` after they sign in.
2. Run `velixo guide` to read the get-started guidance. Run `velixo guide <tool-or-command>` before you use a command for the first time, for example `velixo guide query build` or `velixo guide screen run`.
3. Run `velixo help --all --output json` when you need the flags of every command.
4. Run `velixo connections list` to learn which ERP connections exist. Use `--connection NAME` when more than one fits.

## Rules that apply to every command

- Pass `--intent "<why you make this call>"` on every command that calls a working tool. Write the user's goal in a short sentence. You can set `VELIXO_INTENT` once for a script.
- stdout holds the result: one JSON document when stdout is not a terminal. stderr holds progress and warnings. Parse stdout only.
- In JSON mode, a non-zero exit puts `{ "error": { "code", "exitCode", "message", "taskId", "nextStep" } }` on stdout. `message` is the server's text. `nextStep` is the recovery. Read both.
- Do not show the user a raw `ERROR:` text or an error code. Put the question or the problem to the user in plain words.
- The CLI runs without prompts when stdin is not a terminal or it detects an agent. It never opens a browser and never pages.
- A long task makes the CLI wait and print progress on stderr. To start a task and collect it later, pass `--no-wait` and use `velixo task status <taskId> --wait`.

## Which command replaces which tool

| MCP tool | Command |
|---|---|
| `list_connections` | `velixo connections list` |
| `tool_guidance` | `velixo guide [TOOL...]` |
| `prepare_query` | `velixo query build "REQUEST"` |
| `execute_query` | `velixo query run` |
| `create_workbook_from_query` | `velixo query workbook` |
| `prepare_commands` | `velixo screen build "REQUEST"` |
| `execute_commands` | `velixo screen run` |
| `get_status` | `velixo task status TASKID` |
| `get_upload_url` | `velixo files upload` (named), `velixo files request` (unnamed) |
| `list_staged_files` | `velixo files staged BATCH-TOKEN` |
| `list_import_scenarios` | `velixo import scenarios` |
| `execute_import_scenario` | `velixo import run` |
| `send_notification` | `velixo notify` |
| `sign_out` | `velixo logout` |

Any tool, including one that the server adds later: `velixo call TOOL --args '{...}'`. List the tools with `velixo tools list`.

Downloads have their own command: `velixo files download URL...` fetches a link that the server issued. `velixo screen run --download-dir DIR` fetches the reports a batch produced.

## Data queries

1. Build: `velixo query build "REQUEST" --intent "..." > name.plan.json`. Write every user value in the request as a `{{Placeholder}}`. The result is a plan in `result.data.queries`, written in VQL.
2. Run: `velixo query run --plan name.plan.json --param "Label=Value" --intent "..."`. Run every item of the plan. `--param` takes the display label, the text after `AS` in the query's `DECLARE` line. A label that matches no parameter produces a warning on stderr: read it and fix the label.
3. Read `result.data.rows` and `result.data.columns`. The default window is 250 rows. When `result.data.truncated` is true, read the rest with `--skip`, or use `--all`. Tell the user when you report a partial result.
4. A refusal exits 3. Ask the user the question in plain words, then build again.
5. An ERP error exits 6 with the ERP's message in `error.detail`. Build the query again and quote the message in the request.

Do not edit a query by hand. To change a query, build again with `--base-query FILE`.

## Writes

A command writes to the ERP when it runs a screen batch (`screen run`) or an import scenario (`import run`).

**The write confirmation rule.** Every write needs `--yes`. Follow these steps:

1. Run the build or validation step first: `screen run --dry-run`, or `import run --validate-only`.
2. Tell the user in business words what will change: which record, which values, which connection. Do not paste the batch or the command line.
3. Wait for the user to say yes.
4. Run the write with `--yes`.

The CLI never infers consent. Without a terminal and without `--yes`, a write exits 2 with `CONFIRMATION_REQUIRED` and does nothing. A permission prompt from your own host does not replace the question to the user.

**Duplicate guard.** The CLI refuses to run the same batch again within 10 minutes (exit 2, `DUPLICATE_RUN`). After a failure that you cannot classify, read the record from the ERP before you try again. Add `--force` only when the user wants a second record.

**Result.** The key that the ERP assigned is in `result.feedback.record`. Report it to the user. A refused action (for example `The Release button is disabled.`) exits 6: do not run the batch again.

**Import scenarios.** Show the user the scenario columns from `velixo import scenarios` before they build a file. A partial failure is not atomic: read the ERP back before you report the result.

## Files

- A file you hold: `velixo files upload PATH` prints a `fileToken` that is valid for about an hour. `screen run --attach PATH[:COMMENT]` and `import run --file PATH` stage the file themselves.
- A file the user holds and you do not: `velixo files request` prints an upload page URL. Give it to the user, then run `velixo files staged BATCH-TOKEN --wait`.
- Download links expire in about an hour. Do not print them to the user without need.

## Continuations (Acumatica only)

A continuation is the record that a `screen run` call left open on your screen session, so that a later call can add more lines without reloading the header. Most batches need none: send the header, every detail row and the finish in one call with `--rows FILE`.

Use a continuation only to add a detail that is too long for one call, or to let the user review the header before you add the lines.

1. Start with `--leave-open`. The output holds `result.continuation` and `result.continuationExpiresAt`. The CLI stores them for the next call.
2. Continue with `--continue --rows FILE`. Add `--leave-open` to keep the record open for another chunk.
3. End with `--finish`. The output then has `result.continuation: null` and the assigned key in `result.feedback.record`.
4. Pass `--yes` on every call: each one writes to the open record.

The record stays open for 2 minutes after the last call. Send the chunks one after the other, with no long pause. Do not run another command on the same screen session between chunks. Two parallel records need `--continuation FILE` with a saved continuation for each.

If a call fails with one of these codes, follow `nextStep`:

| Code | Meaning | What to do |
|---|---|---|
| `SCREEN_SESSION_LOST`, nothing ran | The session closed. Unsaved lines are lost. | Start the record again from its header. If the header was saved, load it by key in a new header. |
| `SCREEN_SESSION_LOST`, outcome unknown | The session was lost part-way through the call. | Read the record before you do anything else. |
| `SCREEN_CONTINUATION_MISMATCH` | The session holds another record. | Do not retry. Read what the session holds, or start again. |
| `SCREEN_CONTINUATION_UNVERIFIABLE` | The open record cannot be named. | Start again from the header. |
| `SCREEN_SETS_*` | The sets of the call do not fit together. | The message names the set to remove or add. |

Business Central refuses `--leave-open` and `--continue` (`BC_RECORD_SESSION_UNSUPPORTED`). A Sage Intacct batch runs whole in one call.

## Exit codes

| Code | Meaning | What to do |
|---|---|---|
| 0 | Success. A truncated result or a warning is still a success: read `result.data.truncated` and `warnings`. | Continue. |
| 1 | Unexpected failure: network, HTTP 5xx, a bug. | Retry once, then report. |
| 2 | Usage error: a bad flag, a missing `--intent`, a missing `--yes` in non-interactive mode, a duplicate write without `--force`. | Fix the command line, or ask the user. |
| 3 | The builder refused and asks for a decision or a value. | Ask the user in plain words, then build again. |
| 4 | Not signed in, the session ended, or the Velixo gate denied access (no `mcpServerEnabled`, no credits, no account). | `velixo login`, or relay the reason. |
| 5 | The server refused the call, with a code: `ATTACHMENT_*`, `SCREEN_*`, `IMPORT_*`, `REPORT_*` or `BC_*`. | Read `error.code` and the server guidance. It says whether the ERP was touched. |
| 6 | The task failed in the ERP. | Read `error.detail`. For a query error, build again and quote it. |
| 124 | `--timeout` ran out. The task still runs. | `velixo task status <taskId> --wait` |
| 130 | Interrupted. A running task keeps running on the server. | Check with `velixo task status`. |

## Tell the user what you did

Report in business language: what you asked the ERP, the figures or the record key, and anything that was truncated, skipped or refused. Quote the `warnings` you saw. Never report a write as done until the output shows it.
