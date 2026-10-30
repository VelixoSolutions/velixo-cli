# Command reference

This page lists every command of the Velixo CLI with its flags and output. Run `velixo [command] --help` for the same information in your terminal, or `velixo help --all --output json` for the whole command tree.

Each command that talks to the server calls one tool of the Velixo server with the same argument names. The tool is named under each heading.

## Shared flags

| Flag | Meaning |
|---|---|
| `--profile P` | The profile to use. |
| `--env E` | The environment: `prod` (default), `nx-staging`, `nx-stage-zero`, `nx-dev` or `local`. |
| `--url URL` | The address of any other Velixo server, such as `https://feature-x.velixo.com`. An address without a path gets the server's endpoint path, `/mcp`; a full endpoint URL works as well. `login --url` keeps the address in a profile named after the host. |
| `--connection C` | The ERP connection. Optional on query and ERP-command runs: when you omit it, the CLI uses `VELIXO_CONNECTION`, `.velixo.json` or the profile's default from `velixo connections switch`, and without any of them the server picks the one usable connection, or refuses when several fit. Required on import commands. |
| `--intent TEXT` | Why you make the call: the user's goal. The server records it so that an administrator can see why each call was made. Optional: without it and without `VELIXO_INTENT`, a command that calls a working tool sends `Run from the Velixo CLI: ` and the command name, such as `velixo query run`, and never the argument values. `VELIXO_INTENT` sets it for a whole script. |
| `--output json\|table\|csv` | The output format. `json` is the default when stdout is not a terminal or an AI agent is detected, `table` otherwise. `csv` applies to query rows and any array result. |
| `--compact` | Print the JSON document on one line. |
| `--quiet` | Print no progress on stderr; warnings and errors still print. |
| `--no-color` | Turn colors off. `NO_COLOR` does the same. |
| `--non-interactive` | Never prompt or open a browser. Turns on by itself when stdin is not a terminal or an agent is detected. |
| `--yes` | Confirm a write. |
| `--timeout SECONDS` | Stop waiting after this many seconds (exit code 124). It is one budget for the whole command, however many calls the command makes. |
| `--no-wait` | Print `{ "taskId", "status": "running" }` after the first hold and return. |

A tool returns `{ taskId, status, result }`. The CLI parses the JSON text in `result` and prints it as `result.data`. It keeps `result.attachments`, `result.processStatus`, `result.skippedFields` and `result.feedback` beside it. Text that is not JSON stays a string under `result.text`.

A relative path in any flag or argument is read against the working directory. Exit codes and the error document are in the [README](../README.md); every error code is in [Error codes](#error-codes) below.

## Sign in and status

| Command | Tool | Output |
|---|---|---|
| `velixo login [--env E] [--profile P] [--no-browser] [--complete CODE-OR-URL]` | OAuth gateway | `{ "user": { "email" }, "organization": { "id", "name" }, "environment", "profile", "sessionExpiresAt" }`, or the `authorization_required` document below. |
| `velixo logout [--all]` | `sign_out` | `{ "signedOut": true, "profile" }` |
| `velixo whoami` | none (stored claims) | `{ "user", "organization", "environment", "profile", "sessionExpiresAt", "mcpSessionId" }` |
| `velixo status` | `tools/list`, `list_connections` | `{ "server": { "url", "reachable", "version" }, "signedIn", "tools": 14, "connections": [...], "problems": [] }`. Exit 0 when everything works, 4 when you must sign in, 1 otherwise. |
| `velixo auth token` | none | The refresh token of the current profile on stdout, nothing else. Use it as `VELIXO_TOKEN`. |

`velixo login` opens your browser and waits for the loopback redirect. When the CLI cannot open a browser (no terminal, an agent, `--no-browser`, SSH), it prints this document and exits 0:

```json
{
  "status": "authorization_required",
  "browserUrl": "https://...",
  "expiresAt": "2026-10-28T15:22:00Z",
  "profile": "prod",
  "environment": "prod",
  "nextStep": "velixo login --profile prod --complete <paste the URL the browser lands on>"
}
```

Open the URL on any device and sign in. The browser ends on a page that does not load: copy that page's address and run the `nextStep` command with it.

The CLI signs in as the client `velixo-cli`, which the sign-in page names Velixo CLI, and on a server that does not know that client yet it registers a client of its own once and reuses it; `VELIXO_CLIENT_ID` overrides both.

`velixo logout` ends the gateway session and deletes the stored tokens and state. `--all` does this for every profile.

## Connections and guidance

### `velixo connections list [--status ok]`

Tool: `list_connections`. Prints one entry per connection. `--status ok` keeps the working ones.

```json
[
  { "name": "gab-dev", "type": "acumatica", "status": "ok", "description": "Acumatica 2025 R1, tenant Company" },
  { "name": "cronus/CRONUS US", "type": "business_central", "status": "no_credentials", "description": "..." }
]
```

### `velixo connections switch [NAME | --clear]`

Tool: `list_connections`. Sets the current profile's default connection in `config.json`, the same value as `velixo config set profiles.<name>.connection`. Without `NAME`, a terminal shows the connections with the current default selected: the arrow keys (or `j` and `k`) move, Enter picks, Esc, `q` or Ctrl+C cancel with exit 130 and change nothing. The picker draws on stderr. Without a terminal, or for an agent, `NAME` is required (exit 2). A `NAME` the server does not list is refused (exit 2); the CLI stores the name as the server spells it. `--clear` removes the default and calls no tool. A command uses it when no `--connection`, `VELIXO_CONNECTION` or `.velixo.json` names a connection. `velixo connections list` marks the connection a command would use with `"default": true`, or with `(default)` after its name in a table. Prints:

```json
{ "profile": "prod", "connection": "gab-dev" }
```

### `velixo guide [COMMAND | GROUP | PAGE]...`

No tool. Prints the guide's pages, embedded in the CLI from [docs/guide/](guide/get-started.md), so they match this build's flags and need no sign-in. With no argument it prints the get-started page. `velixo guide query run` prints that command's page, `velixo guide command` every page of the group, and `velixo guide files` a page by its name. A word that names no page exits 2 with the page names. With `--output json` the output is `{ "sections": [{ "name", "commands", "markdown" }] }`.

## Queries

### `velixo query build "REQUEST" [--base-query FILE|-]...`

Tool: `prepare_query`. The request is a natural-language data question. Write every user value as a `{{Placeholder}}`. `--base-query` passes a query you hold, to refine it; repeat it once per query of a multi-query plan, and the CLI sends them as one `baseQuery`, separated by a blank line. The CLI waits for the build (usually 10 to 60 seconds, progress on stderr) and prints the plan:

```json
{
  "taskId": "t_5c1e...",
  "status": "done",
  "result": {
    "data": {
      "queries": [
        { "purpose": "Outstanding AR balance per customer", "query": "DECLARE AsOf SCHEMA [...] AS 'As Of Date' ... SELECT ..." }
      ],
      "note": "Pass 'As Of Date' as YYYY-MM-DD. Natural Net is credit less debit."
    }
  }
}
```

The query is VQL, the Velixo Query Language. VQL is one language for Acumatica, Sage Intacct and Business Central, and a plain GIQL query runs unchanged. Query files use the `.vql` extension. The server still names an item's query text `giql`; the CLI prints it as `query`, so the shape does not change when the server renames it, and `query run` reads either. A plan item can hold `inquiry: { name, parameters }` instead of `query` when the request named a saved inquiry. With `--output table` the items print as a table of purpose and query, with the note under it.

When the builder refuses, the command exits 3 with `error.code: "BUILDER_REFUSAL"` and the `ERROR:` text in `error.message`. The server reports a refusal as a failed build task; the CLI gives it exit 3 all the same. Put the question to the user in plain words, then build again.

Save a plan with `--save FILE`, which writes the same document whatever the output mode (a `.vql` file gets the query text alone, for `query run --query`, when the plan has one query), or with a redirect: `velixo query build "..." > ar-balance.plan.json`. At a terminal the plan prints to read: each item's purpose and whole query, the parameters a run takes, the note, and the `query run` command that runs it, with the `--connection` the build named.

### `velixo query run [NUMBER|NAME] (--plan FILE | --query FILE|- | --inquiry NAME) [flags]`

Tool: `execute_query`. Needs a number or name from `velixo query list`, or exactly one of `--plan`, `--query` and `--inquiry`.

| Flag | Meaning |
|---|---|
| `--plan FILE` | Run every item of a saved plan, in order. The file is the whole output of `query build`, or only its `result.data` object. |
| `--query FILE\|-` | Run one query from a file or from stdin. The CLI sends it as the tool's `giql` argument. |
| `--inquiry NAME` | Run a saved inquiry as saved. |
| `--param "LABEL=VALUE"` | Set a parameter by its display label, the text after `AS` in its `DECLARE` line. The text splits at the first `=`. Repeat for each parameter. |
| `--params FILE\|-` | Set parameters from a JSON object. A `--param` with the same key wins. |
| `--top N` | Rows per call, from 1 to 10,000. Default 250. Sent on every call. |
| `--skip N` | Rows to skip. Default 0. |
| `--all` | Read every window from `--skip` until a page comes back short. Not with `--no-wait`. |
| `--max-rows N` | The limit for `--all`. Default 10,000. |

Before a `--query` or a plan runs, the CLI reads its `DECLARE` lines. A parameter that has a label is keyed by the label; one declared with `TYPE` and no label is keyed by its name. The CLI warns on stderr for every key that matches no parameter, because Acumatica discards an unknown key without an error, and for every `REQUIRED` parameter without a `DEFAULT` that has no value, because the query then matches nothing. A `--inquiry` run is not checked: the CLI cannot see the inquiry's parameters.

In a plan, each query gets only the keys it declares, because Sage Intacct and Business Central refuse a key their query does not take. An item that runs a saved inquiry takes the plan's own parameters: a value given under the inquiry parameter's label replaces it, and each `{{Placeholder}}` in it takes the value given under the placeholder's name, as in `--param "BranchID=HQ"`. A parameter whose placeholder has no value is left out, so the inquiry uses its default, and the CLI warns.

`--all` warns when the query has no `ORDER BY`, because paging an unordered query is not stable. It merges every window into one `rows` array with one `totalCount`. When `--max-rows` stops it before the last row, the merged result carries `truncated: true` and a `warning`.

Output for one query:

```json
{
  "taskId": "t_9a...",
  "status": "done",
  "result": {
    "data": {
      "columns": [
        { "objectName": "ARInvoice", "fieldName": "CustomerID", "caption": "Customer", "type": "string" },
        { "objectName": "ARInvoice", "fieldName": "Balance", "caption": "Natural Net", "type": "decimal", "precision": 2 }
      ],
      "rows": [["ABARTENDE", 12500.00], ["BCORP", 980.15]],
      "totalCount": 2
    }
  }
}
```

Output for a plan, one entry per item in the plan's order:

```json
{
  "note": "Join the two results on CustomerID.",
  "results": [
    { "purpose": "Outstanding AR balance per customer", "result": { "taskId": "t_9a...", "status": "done", "result": { "data": { "columns": [], "rows": [], "totalCount": 0 } } } }
  ]
}
```

With `--output csv` the first line holds the captions and each row follows; a plan of more than one item cannot be written as CSV. With `--output table` the captions head the table and a footer names the window, for example `rows 1 to 250 of 1,204; add --skip 250 or --all for the rest`; a plan prints its note, then each item's purpose and table. `truncated: true` or a `warning` in a result means the ERP matched more rows than the call returned; the CLI repeats the warning on stderr, with any `warnings` the ERP sent.

An HTTP 400 from the ERP, such as a conversion error in the built query, exits 6 with the ERP's message in `error.detail`. Build the query again and quote that message.

### `velixo query workbook [NUMBER|NAME] (--query FILE|- | --plan FILE [--item N]) [--param ...] [--params FILE] [--download PATH]`

Tool: `create_workbook_from_query`. Sends the query text as `query` and the parameters as a JSON object in a string, as the tool takes them; the CLI warns about the parameters as `query run` does. `--item` chooses the plan item, from 1; a plan with one item needs none. Prints the tool's result, a link to a Velixo workbook that holds the VQL query as a live formula:

```json
{
  "result": {
    "data": {
      "attachments": [
        { "fileName": "AR balance.xlsx", "mimeType": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "sizeBytes": 18234, "downloadUrl": "https://app.velixo.com/mcp/download/...", "kind": "queryWorkbook", "path": "ar.xlsx" }
      ],
      "note": "..."
    }
  }
}
```

`--download` fetches the first `downloadUrl` in the result to `PATH`, or into `PATH` under the file's own name when it is a directory, and adds `"path"` to the object that holds the link. It follows the rule of `files download`: a file path is replaced, and a file already in a directory is kept, the new one saved as `name (2).ext`. A plan item that runs a saved inquiry cannot become a workbook: the command exits 2. Build again, asking for that inquiry as a workbook; the builder then returns a VQL query over the inquiry.

## ERP commands

### `velixo command build "REQUEST" [--target SCREEN] [--name NAME] [--save FILE] [--accept-defaults]`

Tool: `prepare_commands`. Describe the operation in the user's words with `{{Placeholders}}` for values. The builder finds the screen. Pass `--target` only with a screen id that an earlier build returned. The output is the batch, a reusable template:

```json
{
  "taskId": "t_11...",
  "status": "done",
  "result": {
    "data": {
      "target": "SO301000",
      "commands": [ { "type": "Clear" }, { "type": "Set", "name": "Document.CustomerID", "value": "{{CustomerID}}" } ],
      "lineCommands": [ "..." ],
      "finishCommands": [ { "type": "Action", "name": "Save" }, { "type": "Get", "name": "Document.OrderNbr" } ],
      "variables": [ { "name": "CustomerID", "type": "string", "description": "The customer the order is for" } ],
      "keyNumbering": { "field": "OrderNbr", "autonumbered": true },
      "commandSets": "..."
    }
  }
}
```

Save it with `--save FILE`, or with a redirect: `velixo command build "..." > create-order.batch.json`. At a terminal the batch prints to read: the screen, whether it writes, the values and detail rows a run takes, how the record gets its key, and the `command run` commands that check and run it, with the `--connection` the build named. When the builder stops with an `ERROR:` question, such as a choice between candidate screens, the command exits 3 with `BUILDER_REFUSAL`, whether the build finished or failed with the question.

`--accept-defaults` adds one sentence to the request: "Leave every field this request does not name at its default." The builder then lets the ERP fill those fields, as the screen would, instead of asking for them. A branch or a project is otherwise one of the values it asks for, because it identifies a business entity. Pass it only when the user said to use the defaults. The library keeps the request as typed, without the sentence. When a `command build` refusal offers to use the defaults, its `nextStep` names the flag, along with `--target` when the refusal gives one.

### `velixo command run [NUMBER|NAME | --batch FILE|-] [flags]`

Tool: `execute_commands`.

| Flag | Meaning |
|---|---|
| `--batch FILE\|-` | The batch from `command build`: the whole output, or only its `result.data`, from a file or, with `-`, from stdin. A batch on stdin cannot answer the confirmation prompt, so a write needs `--yes`. |
| `--param NAME=VALUE` | Bind a placeholder by the name inside the braces. The value is everything after the first `=`. Repeat as needed. An empty value (`--param Note=`) writes a blank. |
| `--param-null NAME` | Pass an explicit null. This drops every command that writes the placeholder, so the ERP sets the field by default. |
| `--params FILE` | Bind variables from a JSON object. `--param` and `--param-null` win over the file. |
| `--rows FILE` | A JSON array of detail rows, `[{ "values": {...}, "rows": [...] }]`, bound to `lineCommands` and `splitCommands`. Use `[]` for no rows. |
| `--attach PATH` | Stage a file with `get_upload_url`, upload it, and attach it to the record. One file per `--attach`; repeat as needed. |
| `--attach-token TOKEN` | Attach a file `files stage` staged, by its token, under the name it was staged with. Repeatable, and combines with `--attach`. |
| `--attach-comment TEXT` | The comment stored with the file of the `--attach` or `--attach-token` just before it. At most one per file; a comment with no file before it exits 2. |
| `--leave-open` | Leave the record open for a later call (see Continuations). |
| `--continue` | Continue the record that the last `--leave-open` call left open. |
| `--continuation FILE` | Continue a record from a saved continuation: the `continuation` object, or a whole saved `command run` result. |
| `--finish` | Run the finish commands on a continued record, or on a `--leave-open` call. |
| `--download-dir DIR` | Fetch every report and file in `result.attachments` into `DIR`, under its `fileName`, and add the full `"path"` to each entry. A file already in `DIR` is kept, as with `files download --dir`. |
| `--dry-run` | Send nothing. Check the batch on your machine and print the summary document (below). Exit 0 when the checks pass. |
| `--yes` | Confirm the write. |
| `--force` | Run a batch that the duplicate guard refused. |

**Command sets.** A first call sends the header `commands`, the `lineCommands` and `splitCommands` with your `--rows`, and the `finishCommands`. With `--leave-open` and no `--finish`, it leaves the finish set out, so the record stays open for more rows. `--param` binds the header and the finish set. Each row's `values` bind the detail set, and each sub-detail row's `values` bind the sub-detail set.

**Checks before any call.** The CLI refuses these without a round trip, with the server's own codes:

| Code | Exit | When |
|---|---|---|
| `SCREEN_VARIABLE_MISSING` | 5 | A placeholder that no `--param`, `--param-null`, `--params` or row value binds. The message lists every problem at once, row by row. |
| `SCREEN_VARIABLE_UNKNOWN` | 5 | Only keys that match no placeholder in the sets this call sends. |
| `SCREEN_SETS_ROWS_WITHOUT_LINE` | 5 | `--rows` for a batch without `lineCommands`. |
| `SCREEN_SETS_LINE_WITHOUT_ROWS` | 5 | A batch with `lineCommands` and no `--rows`. |
| `SCREEN_SETS_SPLIT_ROWS_WITHOUT_SPLIT` | 5 | Sub-detail rows for a batch without `splitCommands`. |

**Dry run.** `--dry-run` prints the confirmation summary on stderr and this document on stdout:

```json
{
  "dryRun": true,
  "connection": "gab-dev",
  "target": "SO301000",
  "commandCount": 9,
  "variables": { "CustomerID": "ACME", "Note": "" },
  "rows": 2,
  "attachments": [ { "fileName": "quote.pdf", "sizeBytes": 18211, "comment": "Signed quote" } ],
  "wouldWrite": true
}
```

`commandCount` counts each set once and the detail and sub-detail sets once per row. `rows` is the number of detail rows. A continuation adds `"continuation"`.

**Confirmation.** Before a batch that can write, the CLI prints a summary on stderr: the connection, the screen, the number of commands, the record it continues and the number of rows, every variable with its value, every attachment and the `keyNumbering` note. On a terminal it then asks `Run this batch? [y/N]`, and any answer other than `y` exits 2 with `CONFIRMATION_DECLINED`. Without a terminal, or with `--non-interactive`, it exits 2 with `error.code: "CONFIRMATION_REQUIRED"` unless `--yes` is present. The CLI treats every `command run` as a write unless the batch sends only `Get` and `Clear` commands. A continuation is always a write.

**Duplicate guard.** The CLI keeps a SHA-256 fingerprint of the connection, the target, the command sets, the variables, the rows and the continued record for 10 minutes after each write it ran. A write that failed in the ERP, timed out or lost the network counts too, because part of it may have saved. It refuses the same fingerprint with exit 2 and `error.code: "DUPLICATE_RUN"` unless you add `--force`.

Output:

```json
{
  "taskId": "t_2b...",
  "status": "done",
  "result": {
    "data": { "...": "the screen's raw envelope" },
    "processStatus": { "status": "completed", "detail": "..." },
    "skippedFields": [ { "variable": "Hold", "field": "Document.Hold" } ],
    "feedback": {
      "record": { "Document": { "OrderType": "SO", "OrderNbr": "SO007231" } },
      "issues": [],
      "savedKey": { "OrderType": "SO", "OrderNbr": "SO007231" },
      "enabledActions": ["Release", "Hold"]
    },
    "attachments": [
      { "kind": "report", "fileName": "SO641010.pdf", "downloadUrl": "https://...", "path": "/work/out/SO641010.pdf" }
    ],
    "continuation": null
  }
}
```

`feedback.savedKey` holds the key that the ERP assigned. When the ERP refuses an action because of the record's state (`The Release button is disabled.`, `... is on hold`, `... requires approval`), the command exits 6 with `error.code: "ERP_ACTION_REFUSED"` and the ERP's sentence in `error.detail`. Do not run the batch again. A task that fails with a server code, such as `SCREEN_SESSION_LOST`, exits 5 with that code. When the batch ran but a `--download-dir` download failed, the command exits with the download's code and its `nextStep` says not to run the batch again.

### Continuations

Most batches need no continuation: one `command run --rows FILE` sends the header, every detail row and the finish in one call. Use a continuation for a detail that is too long for one call (the server takes 50 rows per call), or when you want to review the header before you add the lines. Only Acumatica has screen sessions.

- `--leave-open` leaves the record open. The output has `result.continuation` (`{ screenId, keys, stamp }`) and `result.continuationExpiresAt`, two minutes after the response. The CLI stores the continuation, its deadline and the MCP session id as `lastContinuation` in `state/<profile>.json`.
- `--continue` sends the stored continuation, the `lineCommands` and `splitCommands` from `--batch` and your `--rows`, with no header. Add `--finish` to run the `finishCommands`. A `--param` on such a call binds the finish set only.
- `--continue` with `--leave-open` chains calls. The last chunk carries `--finish`.
- Each response replaces `lastContinuation`. A response without a continuation clears it, because any other call on the screen session ends the open record.
- Each call that writes needs `--yes`.

The CLI exits 5 without a round trip in these cases:

| Code | When | `nextStep` |
|---|---|---|
| `SCREEN_SESSION_LOST` | The stored server session is not the current one, or `continuationExpiresAt` passed more than 10 seconds ago. Nothing ran. | Start the record again from its header. If the header was saved, load it by key in a new header. |
| `SCREEN_CONTINUATION_MISMATCH` | The batch `target` differs from the continuation's `screenId`. | Do not retry. Read what the session holds, or start again. |

The server's own refusals map to exit 5 with the same table: `SCREEN_SESSION_LOST` part-way through a call says "Read the record before doing anything else.", `SCREEN_CONTINUATION_UNVERIFIABLE` says "Start again from the header.", and each `SCREEN_SETS_*` message names the set to remove or add. `--continue` with nothing stored exits 2 with `NO_CONTINUATION`.

```sh
velixo command run --batch order.batch.json --param CustomerID=ACME --rows lines-1.json --leave-open --yes
velixo command run --batch order.batch.json --continue --rows lines-2.json --leave-open --yes
velixo command run --batch order.batch.json --continue --rows lines-3.json --finish --yes
```

## Built queries and batches

Every `query build` and `command build` keeps what it built in a numbered list of the profile, in `library/<profile>.json`, which `logout` leaves alone. The build's output carries the entry's `libraryNumber`, and `libraryName` when `--name NAME` named it. A name is a key: letters, digits, `-` and `_`, starting with a letter; building again with a name moves it to the new build.

| Command | Meaning |
|---|---|
| `velixo query list`, `velixo command list` | Each entry's `number`, `name`, `title`, the `values` a run takes, its `connection` and when it was built. |
| `velixo query run NUMBER\|NAME`, `velixo query workbook NUMBER\|NAME`, `velixo command run NUMBER\|NAME` | Run an entry, on the connection it was built on unless `--connection` names another. At a terminal, with no number, pick it from the list. |
| `velixo query delete [NUMBER\|NAME]`, `velixo command delete [NUMBER\|NAME]` | Delete an entry; at a terminal, with none given, pick it. A number is never reused. |

## Tasks

### `velixo task status TASKID [--wait]`

Tool: `get_status`. Sends no intent. Without `--wait`, the command calls the tool once, which holds up to about 40 seconds, and prints the envelope. With `--wait`, it holds again until the task is `done`, `failed` or `expired` and prints progress on stderr, for example `Release on SO301000, 2m14s, no change for 48s`; `--timeout` still applies and exits 124. The final output has the same shape as the command that started the task: a plan's items carry `query`, and a query result prints as a table or CSV in those formats. A `failed` or `expired` task exits 6 with the server's text in `error.message`. Two failed tasks exit otherwise, in every command: a failed build whose text starts with `ERROR:` is a builder refusal and exits 3, and a text that starts with a server code (`ATTACHMENT_`, `SCREEN_`, `IMPORT_`, `REPORT_`, `BC_`) exits 5 with that code. A task that was started in another organization, or that the server evicted, comes back `expired`.

Every command that starts a task accepts `--no-wait`.

## Files

Files go to the ERP by their path: `command run --attach PATH` and `import run --file PATH` upload them. `files stage` stages a file ahead, or hands out the URL to upload it from elsewhere, and `--attach-token` and `--file-token` use what it staged.

### `velixo files stage FILE [--no-upload] | NAME --size BYTES [--sha256 HEX]`

Tool: `get_upload_url`. Stages one file and prints `{ fileName, sizeBytes, fileToken, expiresAt, uploaded }`. A file on this machine is uploaded, with its size and SHA-256. With `--no-upload`, or for a file that is not on this machine (`--size`, with `--sha256` when you have it), nothing is uploaded and the output adds `uploadUrl` and `curl`, the command that sends the file: `curl -T 'invoice.pdf' 'https://...'` (`curl.exe` in PowerShell). The URL takes the file once; opened in a browser, it shows an upload page.

The profile keeps each token with its file name in `state/<profile>.staged.json`, so `command run --attach-token TOKEN` and `import run --file-token TOKEN` take the token alone. A token expires in about an hour and is spent by the run that files it. A bad name, size or checksum exits 2 before any call.

### `velixo files download URL... [--out PATH | --dir DIR] [--from RESULT.json]`

Fetches download links that the server issued: a rendered report, an ERP attachment or a workbook. The links need no sign-in.

`--from` reads every `attachments[].downloadUrl` from a saved `command run` output. The command searches the whole document for `attachments` arrays, wherever they sit, and downloads each link once. `--out` saves one file to an exact path and replaces a file that is already there. `--dir` (default: the working directory) saves each file under the name the server gives it, from the `Content-Disposition` header or else from the URL. It never overwrites: when `report.pdf` exists, the new file is `report (2).pdf`. Read `path` in the output for the name each file got.

Prints `[{ "fileName", "path", "bytes", "contentType" }]`. A link expires in about an hour. A link the server refuses with a 4xx exits 5 with `ATTACHMENT_TOKEN_INVALID`; run the command that issued the link again.

## Import scenarios (Acumatica)

### `velixo import scenarios --connection C [--screen SCREENID] [--filter PATTERN]`

Tool: `list_import_scenarios`. Needs a connection you state (`--connection`, `VELIXO_CONNECTION`, `.velixo.json` or the profile's default); the server never picks one for an import, and without one the command exits 2. It sends no intent. Prints `[{ name, screenId, apiEnabled, columns }]`. `columns` is absent when `apiEnabled` is false. Show the columns to the user before they build a file.

On a terminal, the default output is a table with the columns name, screen, apiEnabled and columns, the last one joined by commas.

### `velixo import run --connection C --scenario NAME (--file PATH | --file-token TOKEN) [flags]`

Tool: `execute_import_scenario`. Needs a connection you state, as `import scenarios` does. `--file-token` imports a file `files stage` staged, in place of `--file`.

| Flag | Meaning |
|---|---|
| `--sheet NAME` | The sheet of a workbook file. |
| `--range A1:F200` | The cell range to import. |
| `--validate-only` | Stop after the preview and print it on stdout. |
| `--break-on-error` | Stop at the first row that fails. |
| `--yes` | Confirm the real run. |

The command runs the fixed sequence:

1. It uploads the file from its path, or takes the staged file of `--file-token`.
2. It calls the tool with `validateOnly: true`. The preview prints on stderr: scenario, screen, connection, row count and every warning, then a reminder that an import is not atomic.
3. With `--validate-only`, it prints the preview on stdout and exits 0. The preview is `{ "result": { "data": { "scenario", "screenId", "screenTitle", "file": { "name", "sheet", "range", "rows" }, "columns", "sample", "warnings", "executed": false } } }`.
4. It asks for confirmation: on a terminal, `Import 15 rows with scenario Import Customers into screen AR303000 on connection gab-dev? [y/N]`, and any answer other than `y` exits 2 with `CONFIRMATION_DECLINED`. With `--yes` it does not ask. Without a terminal and without `--yes` it exits 2 with `CONFIRMATION_REQUIRED`, after the preview has printed.
5. It calls the tool again without `validateOnly` on the same upload and follows the task, with progress on stderr.

A real run spends the upload: a second run with `--file` uploads again, and a second run with `--file-token` needs a new token. If the server answers `ATTACHMENT_TOKEN_ALREADY_USED`, the import already happened: the command exits 5 and its `nextStep` says to read the ERP first. An `IMPORT_*` refusal exits 5 with its code; each one names what to change in the file.

The output is the import report. A task that finishes inside the first hold prints the same document as a long one:

```json
{
  "taskId": "t_7d...",
  "status": "done",
  "result": {
    "data": {
      "scenario": "Import Customers",
      "screenId": "AR303000",
      "executed": true,
      "totalRows": 15,
      "processed": 14,
      "failed": 1,
      "notAttempted": 0,
      "rows": [
        { "row": 2, "processed": true, "keys": { "CustomerID": "CA000123" }, "error": null },
        { "row": 9, "processed": false, "keys": null, "error": "'Customer Class' cannot be empty." }
      ],
      "failures": [ { "row": 9, "error": "'Customer Class' cannot be empty." } ],
      "warnings": [],
      "durationMs": 4210
    }
  }
}
```

Row numbers are the lines of your file, the header being line 1. A report with failed rows still exits 0. The command then prints a warning on stderr that names how many rows failed. A partial failure is not atomic: read the ERP back before you report the result.

## Notifications

### `velixo notify --subject TEXT --body TEXT [flags]`

Tool: `send_notification`.

| Flag | Meaning |
|---|---|
| `--target EMAIL\|TEAMS-CHANNEL-LINK` | Where to send it. Default: the signed-in user. |
| `--payload FILE` | A saved `query run` output. The CLI sends its `result.data` unchanged as one JSON string, or the whole file when it is a query result with `columns` and `rows`. Each surface draws the table itself. Any other file exits 2. |
| `--link URL` | A link to include. |
| `--link-label TEXT` | The text of the link. |
| `--severity Info\|Warning\|Error` | The severity. |
| `--source-name NAME` | The sender name. Default `velixo-cli`. |

Prints the server's answer: `{ accepted, reachedNobody, recipients: [{ name, channels }], note }`. When `reachedNobody` is true, the notification is recorded but nothing pushed it to anyone, and the CLI prints a warning on stderr. Do not report the recipient as notified.

## Discovery

| Command | Meaning |
|---|---|
| `velixo tools list [--names]` | Calls `tools/list`. Prints the tools with their input schemas, or only the names. |
| `velixo tools guidance [TOOL...]` | Calls `tool_guidance`. Prints the server's own guidance, written for its other clients; `get_started` with no argument. A command name works as a tool name: `command run` is `execute_commands`. `--output json` splits it into `{ "sections": [{ "name", "markdown" }] }`. |
| `velixo help [COMMAND...] [--all] [--output json]` | Prints the help of one command, or with `--all` the whole command tree with flags and one-line descriptions. |

## Configuration and version

| Command | Meaning |
|---|---|
| `velixo config get\|set\|list\|path` | Reads and writes `config.json`. `path` prints its location. |
| `velixo version [--check]` | Prints the version. With `--check`, also prints the latest release. |

## Error codes

`error.code` is one of these codes, or a code the server sent, which the CLI passes on unchanged. A server code in the `ATTACHMENT_`, `SCREEN_`, `IMPORT_`, `REPORT_` or `BC_` family always exits 5, whether the tool returned it as an error or a task failed with it.

| Code | Exit | Meaning |
|---|---|---|
| `USAGE` | 2 | A bad flag or argument, or an input file that is missing or not valid. |
| `CONFIRMATION_REQUIRED` | 2 | A write without `--yes` and without a terminal to ask on. Nothing ran. |
| `CONFIRMATION_DECLINED` | 2 | The person answered no at the terminal prompt. Nothing ran. |
| `DUPLICATE_RUN` | 2 | The same screen write ran within the last 10 minutes. `--force` runs it again. |
| `NO_CONTINUATION` | 2 | `command run --continue` with no record left open on this profile. |
| `CONFIG_INVALID` | 2 | `config.json` or `.velixo.json` is not valid JSON. |
| `BUILDER_REFUSAL` | 3 | A builder answered with an `ERROR:` question. `message` holds the question verbatim. |
| `NOT_SIGNED_IN` | 4 | No sign-in on the profile, or the server does not know the caller. |
| `SESSION_ENDED` | 4 | The 30-day session ended, or the server refused the token even after a refresh. |
| `ACCESS_DENIED` | 4 | Velixo refused the caller: no access to the AI tools, no credits or no account. |
| `SIGN_IN_FAILED` | 4 | The sign-in did not complete. Exit 1 when the sign-in server itself failed. |
| `CREDENTIALS_INVALID` | 4 | `credentials.json` is not valid. Sign in again. |
| `TOOL_ERROR` | 5 | The tool returned an error without a code. When the server refuses because several connections fit, `nextStep` names `velixo connections switch NAME`. |
| `ATTACHMENT_UPLOAD_FAILED` | 5 | The upload endpoint refused a file's bytes with a 4xx. |
| `ATTACHMENT_TOKEN_INVALID` | 5 | A download link or a staged file's token expired, was spent or is not the caller's. Run again with `--attach` or `--file`, or stage the file again for a new token. |
| `ATTACHMENT_TOKEN_ALREADY_USED` | 5 | The staged file is already on a record, so nothing was sent again. Read the ERP first. |
| `SCREEN_VARIABLE_MISSING` | 5 | A placeholder has no value. The CLI checks it before the call. |
| `SCREEN_VARIABLE_UNKNOWN` | 5 | A value names no placeholder. The CLI checks it before the call. |
| `SCREEN_SESSION_LOST` | 5 | The screen session that held the open record is gone. |
| `SCREEN_CONTINUATION_MISMATCH` | 5 | The session holds another record than the continuation names. |
| `SCREEN_CONTINUATION_UNVERIFIABLE` | 5 | The open record cannot be named. |
| `SCREEN_SETS_ROWS_WITHOUT_LINE`, `SCREEN_SETS_LINE_WITHOUT_ROWS`, `SCREEN_SETS_SPLIT_ROWS_WITHOUT_SPLIT` | 5 | The batch's command sets and the rows do not fit together. The CLI checks them before the call. |
| `BC_RECORD_SESSION_UNSUPPORTED` | 5 | Business Central keeps no record open, so `--leave-open` and `--continue` do not apply. |
| `TASK_FAILED` | 6 | The task failed in the ERP. `detail` holds the ERP's text. |
| `TASK_EXPIRED` | 6 | The server no longer knows the task. |
| `ERP_ACTION_REFUSED` | 6 | The ERP refused an action because of the record's state. Do not run the batch again. |
| `NETWORK_ERROR` | 1 | The server could not be reached, or a transfer stopped part-way. |
| `HTTP_ERROR` | 1 | An HTTP 5xx or another answer the CLI cannot use. |
| `MCP_PROTOCOL_ERROR` | 1 | The server answered in a shape the CLI does not know. |
| `INTERNAL_ERROR` | 1 | A bug in the CLI. |
| `TIMEOUT` | 124 | `--timeout` ran out. A task keeps running on the server. |
| `INTERRUPTED` | 130 | Ctrl-C. A task keeps running on the server. |
