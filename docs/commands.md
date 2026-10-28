# Command reference

This page lists every command of the Velixo CLI with its flags and output. Run `velixo [command] --help` for the same information in your terminal, or `velixo help --all --output json` for the whole command tree.

Each command that talks to the server calls one tool of the Velixo MCP server with the same argument names. The tool is named under each heading.

## Shared flags

| Flag | Meaning |
|---|---|
| `--profile P` | The profile to use. |
| `--env E` | The environment: `prod` (default), `nx-staging`, `nx-stage-zero`, `nx-dev` or `local`. |
| `--url URL` | The MCP URL of any other server. |
| `--connection C` | The ERP connection. Optional on query and screen commands: when you omit it, the server picks the one usable connection, or refuses when several fit. Required on import commands. |
| `--intent TEXT` | Why you make the call. Required on every command that calls a working tool. The server refuses a call without it. `VELIXO_INTENT` sets it for a whole script. |
| `--output json\|table\|csv` | The output format. `json` is the default when stdout is not a terminal, `table` when it is. `csv` applies to query rows and any array result. |
| `--compact` | Print the JSON document on one line. |
| `--quiet` | Silence stderr except errors. |
| `--no-color` | Turn colors off. `NO_COLOR` does the same. |
| `--no-pager` | Do not page long output on a terminal. |
| `--non-interactive` | Never prompt, open a browser or page. Turns on by itself when stdin is not a terminal or an agent is detected. |
| `--yes` | Confirm a write. |
| `--timeout SECONDS` | Stop waiting for a task after this many seconds (exit code 124). |
| `--no-wait` | Print `{ "taskId", "status": "running" }` after the first hold and return. |

A tool returns `{ taskId, status, result }`. The CLI parses the JSON text in `result` and prints it as `result.data`. It keeps `result.attachments`, `result.processStatus`, `result.skippedFields` and `result.feedback` beside it. Text that is not JSON stays a string under `result.text`.

Exit codes, the error document and the environment variables are in the [README](../README.md).

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
  "userCode": "...",
  "expiresAt": "2026-10-28T15:22:00Z",
  "nextStep": "velixo login --complete ..."
}
```

Open the URL on any device and sign in. Then run `velixo login --complete <code-or-url>`. Until the device-code flow ships, paste the URL that the browser was redirected to.

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

### `velixo guide [get_started] [TOOL...] [--cli]`

Tool: `tool_guidance`. Prints the Markdown sections of the server's tool guidance, one after the other, as text. `velixo guide` with no argument prints `get_started`. A command name works as well as a tool name: `velixo guide screen run` prints the `execute_commands` section. With `--output json` the output is `{ "sections": [{ "name", "markdown" }] }`. The CLI holds no copy of the guidance.

`velixo guide --cli` prints the CLI's own agent guide, [agent-guide.md](agent-guide.md).

## Queries

### `velixo query build "REQUEST" [--base-query FILE|-]...`

Tool: `prepare_query`. Needs `--intent`. The request is a natural-language data question. Write every user value as a `{{Placeholder}}`. `--base-query` passes a query you hold, to refine it; repeat it once per query of a multi-query plan, and the CLI sends them as one `baseQuery`, separated by a blank line. The CLI waits for the build (usually 10 to 60 seconds, progress on stderr) and prints the plan:

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

Save a plan with a redirect: `velixo query build "..." > ar-balance.plan.json`.

### `velixo query run (--plan FILE | --query FILE|- | --inquiry NAME) [flags]`

Tool: `execute_query`. Needs `--intent` and exactly one of `--plan`, `--query` and `--inquiry`.

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

### `velixo query workbook (--query FILE|- | --plan FILE [--item N]) [--param ...] [--params FILE] [--download PATH]`

Tool: `create_workbook_from_query`. Needs `--intent`. Sends the query text as `query` and the parameters as a JSON object in a string, as the tool takes them; the CLI warns about the parameters as `query run` does. `--item` chooses the plan item, from 1; a plan with one item needs none. Prints the tool's result, a link to a Velixo workbook that holds the VQL query as a live formula:

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

`--download` fetches the first `downloadUrl` in the result to `PATH`, or into `PATH` under the file's own name when it is a directory, and adds `"path"` to the object that holds the link. A plan item that runs a saved inquiry cannot become a workbook: the command exits 2. Build again, asking for that inquiry as a workbook; the builder then returns a VQL query over the inquiry.

## Screen commands (Acumatica)

### `velixo screen build "REQUEST" [--target SCREEN]`

Tool: `prepare_commands`. Describe the operation in the user's words with `{{Placeholders}}` for values. The builder finds the screen. The output is the batch, a reusable template:

```json
{
  "taskId": "t_11...",
  "status": "done",
  "result": {
    "data": {
      "target": "SO301000",
      "commands": [ { "op": "Clear" }, { "op": "Set", "view": "Document", "field": "CustomerID", "value": "{{CustomerID}}" } ],
      "lineCommands": [ "..." ],
      "finishCommands": [ { "op": "Save" }, { "op": "Get", "view": "Document", "field": "OrderNbr" } ],
      "variables": [ { "name": "CustomerID", "type": "string", "description": "The customer the order is for" } ],
      "keyNumbering": "Acumatica assigns OrderNbr on save",
      "note": "..."
    }
  }
}
```

Save it: `velixo screen build "..." > create-order.batch.json`. A refusal that names candidate screens exits 3.

### `velixo screen run --batch FILE [flags]`

Tool: `execute_commands`.

| Flag | Meaning |
|---|---|
| `--batch FILE` | The batch file from `screen build`. |
| `--var NAME=VALUE` | Bind a placeholder by the name inside the braces. Repeat as needed. An empty value (`--var Note=`) writes a blank. |
| `--var-null NAME` | Pass an explicit null. This drops every command that writes the placeholder, so the ERP sets the field by default. |
| `--vars FILE` | Bind variables from a JSON file. |
| `--rows FILE` | A JSON array of detail rows, `[{ "values": {...}, "rows": [...] }]`, bound to `lineCommands` and `splitCommands`. |
| `--attach PATH[:COMMENT]` | Stage a file and attach it to the record. Repeat as needed. |
| `--leave-open` | Leave the record open for a later call (see Continuations). |
| `--continue` | Continue the record that the last `--leave-open` call left open. |
| `--continuation FILE` | Continue with a continuation that you saved. |
| `--finish` | Run the finish commands on a continued record. |
| `--download-dir DIR` | Fetch every report and file in `result.attachments` into `DIR` and add `"path"` to each entry. |
| `--dry-run` | Send nothing. Bind the variables on your machine, report every unbound placeholder and unknown key (`SCREEN_VARIABLE_MISSING`, `SCREEN_VARIABLE_UNKNOWN`) and print the confirmation summary. Exit 0 when the server would accept the batch. |
| `--yes` | Confirm the write. |
| `--force` | Run a batch that the duplicate guard refused. |

**Confirmation.** Before a batch that can write, the CLI prints a summary on stderr: the connection, the screen, the number of commands, every variable with its value, every attachment and the `keyNumbering` note. On a terminal it then asks `Run this batch? [y/N]`. Without a terminal it exits 2 with `error.code: "CONFIRMATION_REQUIRED"` unless `--yes` is present. The CLI treats every `screen run` as a write unless the batch holds only `Get` and `Clear` commands.

**Duplicate guard.** The CLI keeps a fingerprint of connection, target, commands, variables and rows for 10 minutes after each executed write. It refuses the same fingerprint with exit 2 and `error.code: "DUPLICATE_RUN"` unless you add `--force`.

Output:

```json
{
  "taskId": "t_2b...",
  "status": "done",
  "result": {
    "data": { "...": "the screen's raw envelope" },
    "processStatus": null,
    "skippedFields": ["Document.Hold"],
    "feedback": {
      "record": { "OrderType": "SO", "OrderNbr": "SO007231" },
      "issues": [],
      "allowedActions": ["Release", "Hold"]
    },
    "attachments": [
      { "kind": "report", "fileName": "SO641010.pdf", "downloadUrl": "https://...", "expiresAt": "...", "path": "out/SO641010.pdf" }
    ],
    "continuation": null
  }
}
```

`feedback.record` holds the key that the ERP assigned. When the ERP refuses an action (`The Release button is disabled.`), the command exits 6 with the ERP's sentence. Do not run the batch again.

### Continuations

Most batches need no continuation: one `screen run --rows FILE` sends the header, every detail row and the finish in one call. Use a continuation for a detail that is too long for one call, or when you want to review the header before you add the lines. Only Acumatica has screen sessions.

- `--leave-open` leaves the record open. The output has `result.continuation` (`{ screenId, keys, stamp }`) and `result.continuationExpiresAt`, two minutes after the response. The CLI stores both as `lastContinuation` in `state/<profile>.json`.
- `--continue` sends the stored continuation, the `lineCommands` and `splitCommands` from `--batch` and your `--rows`, with no header. Add `--finish` to run the `finishCommands`. A `--var` on such a call binds the finish set only.
- `--continue` with `--leave-open` chains calls. The last chunk carries `--finish`.
- Each call that writes needs `--yes`.

The CLI exits 5 without a round trip when the stored MCP session id is no longer current, when `continuationExpiresAt` has passed (`SCREEN_SESSION_LOST`, nothing ran), or when the batch `target` differs from the continuation's `screenId` (`SCREEN_CONTINUATION_MISMATCH`).

```sh
velixo screen run --batch order.batch.json --var CustomerID=ACME --rows lines-1.json --leave-open --yes
velixo screen run --batch order.batch.json --continue --rows lines-2.json --leave-open --yes
velixo screen run --batch order.batch.json --continue --rows lines-3.json --finish --yes
```

## Tasks

### `velixo task status TASKID [--wait]`

Tool: `get_status`. Needs no `--intent`. Without `--wait`, the command calls the tool once, which holds up to about 40 seconds, and prints the envelope. With `--wait`, it holds again until the task is `done`, `failed` or `expired` and prints progress on stderr, for example `Release on SO301000, 2m14s, no change for 48s`; `--timeout` still applies and exits 124. The final output has the same shape as the command that started the task: a plan's items carry `query`, and a query result prints as a table or CSV in those formats. A `failed` or `expired` task exits 6 with the server's text in `error.message`; a failed build whose text starts with `ERROR:` is a builder refusal and exits 3. A task that was started in another organization, or that the server evicted, comes back `expired`.

Every command that starts a task accepts `--no-wait`.

## Files

### `velixo files upload PATH... [--comment TEXT]`

Tool: `get_upload_url` (named mode). For each file, the command computes the size and the SHA-256, asks for an upload link with them, and streams the bytes to that link with an HTTP PUT. It needs no `--intent`. It prints one entry per file:

```json
[
  { "fileName": "invoice-4411.pdf", "sizeBytes": 183220, "sha256": "...", "fileToken": "ut_...", "expiresAt": "2026-10-28T16:10:00Z" }
]
```

`--comment` adds a `comment` to each printed entry. It is the note a later `execute_commands` call stores beside the file on the record; the upload itself sends no comment.

A file the server will not stage (a type or size it refuses) exits 5 with the server's code, such as `ATTACHMENT_TYPE_NOT_ALLOWED`. An upload endpoint that refuses the bytes exits 5 with `ATTACHMENT_UPLOAD_FAILED` and the HTTP status. A file that does not exist exits 2 before any call.

`screen run --attach` and `import run` stage files themselves. Use this command to stage first and run later, within the hour.

### `velixo files request [--max-files N]`

Tool: `get_upload_url` (unnamed mode). Prints `{ uploadPageUrl, uploadBatchToken, maxFiles, expiresAt }`. `--max-files` is sent only when you give it. Give the page URL to a user who holds the file and wants to upload it.

### `velixo files staged BATCH-TOKEN [--wait]`

Tool: `list_staged_files`. Prints `{ maxFiles, submitted, expiresAt, note, files: [{ fileName, sizeBytes, fileToken, alreadyFiled }] }`.

Without `--wait`, the command makes one call that does not hold. With `--wait`, it calls again until `submitted` is true or the server returns a `note`, and prints progress on stderr. A `note` also prints as a warning on stderr: it says what to ask the user, so ask it and do not call the command again until they answer. `--timeout` limits the wait and exits 124.

### `velixo files download URL... [--out PATH | --dir DIR] [--from RESULT.json]`

Fetches download links that the server issued: a rendered report, an ERP attachment or a workbook. The links need no sign-in.

`--from` reads every `attachments[].downloadUrl` from a saved `screen run` output. The command searches the whole document for `attachments` arrays, wherever they sit, and downloads each link once. `--out` saves one file to an exact path and replaces a file that is already there. `--dir` (default: the working directory) saves each file under the name the server gives it, from the `Content-Disposition` header or else from the URL. It never overwrites: when `report.pdf` exists, the new file is `report (2).pdf`. Read `path` in the output for the name each file got.

Prints `[{ "fileName", "path", "bytes", "contentType" }]`. A link expires in about an hour. A link the server refuses with a 4xx exits 5 with `ATTACHMENT_TOKEN_INVALID`; run the command that issued the link again.

## Import scenarios (Acumatica)

### `velixo import scenarios --connection C [--screen SCREENID] [--filter PATTERN]`

Tool: `list_import_scenarios`. `--connection` is required here: without it the command exits 2. It needs no `--intent`. Prints `[{ name, screenId, apiEnabled, columns }]`. `columns` is absent when `apiEnabled` is false. Show the columns to the user before they build a file.

On a terminal, the default output is a table with the columns name, screen, apiEnabled and columns, the last one joined by commas.

### `velixo import run --connection C --scenario NAME --file PATH [flags]`

Tool: `execute_import_scenario`. Requires `--connection` and an `--intent`.

| Flag | Meaning |
|---|---|
| `--sheet NAME` | The sheet of a workbook file. |
| `--range A1:F200` | The cell range to import. |
| `--validate-only` | Stop after the preview and print it on stdout. |
| `--break-on-error` | Stop at the first row that fails. |
| `--yes` | Confirm the real run. |

The command runs the fixed sequence:

1. It stages the file, as `files upload` does.
2. It calls the tool with `validateOnly: true`. The preview prints on stderr: scenario, screen, connection, row count and every warning, then a reminder that an import is not atomic.
3. With `--validate-only`, it prints the preview on stdout and exits 0. The preview is `{ "result": { "data": { "scenario", "screenId", "screenTitle", "file": { "name", "sheet", "range", "rows" }, "columns", "sample", "warnings", "executed": false } } }`.
4. It asks for confirmation: on a terminal, `Import 15 rows with scenario Import Customers into screen AR303000 on connection gab-dev? [y/N]`. With `--yes` it does not ask. Without a terminal and without `--yes` it exits 2 with `CONFIRMATION_REQUIRED`, after the preview has printed.
5. It calls the tool again without `validateOnly` on the same upload and follows the task, with progress on stderr.

A real run spends the upload, so a second run uploads again. If the server answers `ATTACHMENT_TOKEN_ALREADY_USED`, the import already happened: the command exits 5 and its `nextStep` says to read the ERP first and to upload again only if the file must run again. An `IMPORT_*` refusal exits 5 with its code; each one names what to change in the file.

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

Tool: `send_notification`. Requires an `--intent`.

| Flag | Meaning |
|---|---|
| `--target EMAIL\|TEAMS-CHANNEL-LINK` | Where to send it. Default: the signed-in user. |
| `--payload FILE` | A saved `query run` output. The CLI sends its `result.data` unchanged as one JSON string, or the whole file when it is a query result with `columns` and `rows`. Each surface draws the table itself. Any other file exits 2. |
| `--link URL` | A link to include. |
| `--link-label TEXT` | The text of the link. |
| `--severity Info\|Warning\|Error` | The severity. |
| `--source-name NAME` | The sender name. Default `velixo-cli`. |

Prints the server's answer: `{ accepted, reachedNobody, recipients: [{ name, channels }], note }`. When `reachedNobody` is true, the notification is recorded but nothing pushed it to anyone, and the CLI prints a warning on stderr. Do not report the recipient as notified.

## Raw access and discovery

| Command | Meaning |
|---|---|
| `velixo tools list [--names]` | Calls `tools/list`. Prints the tools with their input schemas, or only the names. |
| `velixo call TOOL [--args JSON \| --args @FILE \| --stdin]` | Calls any tool with the arguments as given. Prints the result with the same unwrapping as the commands. A tool error exits 5 with the text in `error.message`. |
| `velixo help --all [--output json]` | Prints the whole command tree with flags and one-line descriptions. |

## Configuration and version

| Command | Meaning |
|---|---|
| `velixo config get\|set\|list\|path` | Reads and writes `config.json`. `path` prints its location. |
| `velixo version [--check]` | Prints the version. With `--check`, also prints the latest release. |
