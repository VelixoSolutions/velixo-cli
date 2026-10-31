<!-- commands: command run -->
# velixo command run

`velixo command run 2` runs batch 2 of the profile's list, on the connection it was built on; `--batch FILE` runs a batch saved with `velixo command build --save`, and `--batch -` reads one from stdin. A file does not remember its connection: pass the `--connection` you built with. A batch can read fields, run a report, read files or write. You bind the batch's placeholders with flags. It is the only command that changes a record through a screen, so treat every batch as a possible write.

## Example

```sh
velixo command run 2 --dry-run \
  --param CustomerID=ABCSTUDIOS --param DocDate=2026-09-30 --param Amount=1500.00
# tell the user what will change, wait for their yes, then:
velixo command run 2 --yes --intent "Bill ABC Studios for the September retainer" \
  --param CustomerID=ABCSTUDIOS --param DocDate=2026-09-30 --param Amount=1500.00
```

## Before a write

1. **Check first, then ask.** Run `--dry-run` with the values you will use. It sends nothing, checks every binding, and prints `{ dryRun, connection, target, commandCount, variables, rows, attachments, wouldWrite }`. `wouldWrite: false` means the batch only reads. `connection` is `null` when the default applies: name the connection marked `default` in `velixo connections list` when you ask.
2. **Ask the user in business words**, never by pasting the batch or the command line:
   - A one-line headline of the outcome and where it happens: "Create an AR invoice for ABC Studios for 1,500.00 on the Invoices and Memos screen".
   - What changes and what it means for the business, with how many records are affected.
   - **Every business entity the batch touches that the user did not state** (customer, vendor, branch, warehouse, project, employee, location), and where the value came from.
   - **A plain warning when the step cannot be undone** (release, post, delete, confirm shipment), or when a partial failure could leave part of the work saved.
   - Each file to attach and the record it goes on. One question covers the record and its files.
   - When `keyNumbering` says `autonumbered: true` and the user named an ID: Acumatica assigns the ID, and theirs will not be used.
   - Offer two choices: the action itself ("Create the invoice") and "No". Say the exact commands are available if they ask.
3. **Wait for the yes**, then run with `--yes`. A permission prompt from your own host is not the user's yes.

Without a terminal and without `--yes`, a write exits 2 with `CONFIRMATION_REQUIRED` and nothing runs.

A batch that only reads runs without `--yes`: the dry run shows `wouldWrite: false`. The server says whether a batch it built writes, in the build's `writes`, and the CLI follows it. The CLI also checks the commands, because a batch file can be edited after its build, and counts the batch as a write when either says so. A read is a batch of `Get`, `Clear`, `Key`, `ListAttachments`, `DownloadAttachments` and `Set` commands, and of the actions that persist nothing: `First`, `Previous`, `Next`, `Last`, `Cancel`, `PreviousPeriod`, `NextPeriod`, `Refresh` and `RunReport`. It has no other action such as `Save`, no `UploadAttachment`, no `commit` and no `Set` on a note, so the screen keeps none of it, and a report run is a read. On Business Central and Sage Intacct, which keep no record open, a `Set` or an action is a write unless the build's `writes` says otherwise. A run that attaches a file, leaves its record open or continues one is always a write. Any other batch is a write.

**Every run is a new write.** Running the same batch again creates a second invoice, or makes the same change twice. "The same invoice for BCORP" is a second invoice and needs its own yes. The CLI does not stop a second run, so after a failure, read the record before you run anything again, and run it again only when the user wants it written again.

## Values: --param, --param-null, --params, --rows

- `--param NAME=VALUE` binds `{{NAME}}`, the token inside the braces. Names are case-sensitive. The value is everything after the first `=`.
- `--param-null NAME` lets Acumatica set the field by default. It drops every command that writes that placeholder, so the fields that default from it follow (leave the employee unset on an expense claim, and Acumatica fills the claimant and the branch from the employee's record). This is how you say "use the default" for a field the batch writes. When the build itself asked for a value, such as a branch or a project, the user's "use the defaults" goes to `command build --accept-defaults` instead. The output lists what was dropped in `result.skippedFields`: read those fields back and tell the user what the ERP chose.
- **Null is not blank.** `--param NAME=` writes an empty string over the default and breaks the chain of defaults. Use it only when the user asked to clear the field.
- **A missing name is not null.** A placeholder you bind with nothing exits 5 with `SCREEN_VARIABLE_MISSING`. Skipping a field must be something you asked for.
- `--params FILE` reads a JSON object of names and values. `--param` and `--param-null` win over it.
- `--rows FILE` gives the detail rows of a batch with `lineCommands`: a JSON array, one entry per line, in order.

```json
[
  { "values": { "Account": "60000", "DebitAmt": "250.00", "CreditAmt": "0" } },
  { "values": { "Account": "20000", "DebitAmt": "0", "CreditAmt": "250.00" },
    "rows": [ { "values": { "SplitQty": "1" } } ] }
]
```

Each row binds its own placeholders in `values`. A row's `rows` are its sub-detail rows, for `splitCommands`. `--param` binds the header and the finish set only. A batch with `lineCommands` needs `--rows`; pass a file holding `[]` for no lines. The whole document is then one call and one save, and every row is checked before anything runs.

These are refused before anything runs. The CLI checks the first three on your machine:

| Code | Exit | What to do |
|---|---|---|
| `SCREEN_VARIABLE_MISSING` | 5 | Bind every name the message lists, row by row. |
| `SCREEN_VARIABLE_UNKNOWN` | 5 | Remove the names no placeholder takes: a stale batch or a wrong case. |
| `SCREEN_SETS_*` | 5 | The rows and the batch's sets do not fit. The message names the fix. |
| `SCREEN_VARIABLE_ILLEGAL_VALUE` | 5 | A value holds `<` or `>`. Values are field data, never structure. |
| `SCREEN_VARIABLE_MISPLACED`, `SCREEN_VARIABLE_NOT_WHOLE_VALUE`, `SCREEN_VARIABLE_MALFORMED` | 5 | The batch is malformed. Build it again; never edit it. |
| `SCREEN_VARIABLE_NOT_SKIPPABLE` | 5 | That null cannot be dropped (a key, a row selector, the command that loads the record). Give a value, or build again without the field. |
| `SCREEN_RECORD_NOT_FOUND` | 5 | No record holds the key the batch changes. The key is probably mistyped: ask the user. |
| `SCREEN_RECORD_EXISTS` | 5 | A record already holds the ID the batch creates. Ask the user: update that record, or use another ID. |

Never invent an identifier to get a batch to run. If a value the batch needs was not given, ask.

## By number, and at a terminal

`velixo command run 2` runs batch 2 from the profile's list (see `velixo guide library`) with the connection it was built on. At a terminal, `velixo command run` with no number shows the list to pick from.

At a terminal, the run asks for every value no flag gave, one at a time, with the type and description the build gave it. Type `-` to leave a field to the ERP's default, as `--param-null` does. For a batch with lines and no `--rows`, it then asks for a line at a time ("Add a line?"), with its sub-lines when the batch has them. It asks nothing on a `--continue`, for a batch read from stdin, without a terminal, or for an agent: those pass every value as a flag.

## What comes back

- `result.feedback.savedKey`: the key of the saved record, including an ID Acumatica assigned. Report it, and use it to name the record in later batches.
- `result.feedback.record`: the values the ERP recalculated (totals, prices, defaults).
- `result.feedback.issues`: field errors and warnings. Relay every one, warnings included.
- `result.feedback.enabledActions`: the actions the record allows now. Offer a next step only from this list.
- `result.processStatus`: the final outcome of a long process such as a release or a post. **`failed` means the action did not happen, even with exit 0.** Read `detail` and relay it. On `stillRunning` or `probeFailed`, read the document's status with a read-only batch. When the process settled, the reads at the end of the batch were taken again afterwards, so they are the document's state now.
- `result.attachments`: reports and files, as links.
- `result.data`: the screen's raw response.

Never report a write as done until the output shows it.

## Reports and files

A report, or a file read off a record, arrives as a link in `result.attachments`, with `kind` `report` or `erpAttachment`. The file is the deliverable:

- Pass `--download-dir DIR` to save each file under its name; the output adds its `path`. Tell the user where it is and which format it has. Or save the output and run `velixo files download --from FILE`.
- Never summarise or reformat a report in place of delivering it, and never describe a file you only hold a link to.
- A link expires in about an hour and is bound to this session. Do not repeat it into a later message. Run the batch again rather than send a stale link.
- File reads write nothing. When one falls short, say which way: `ATTACHMENT_NO_MATCHES` with no files on the record means the record has none; with files on it, your filter matched none, so list again without one. A `refused` list names files over 32 MB: tell the user they are on the record in Acumatica. A note that more files were not delivered means the cap of 20 links was reached: never present that as the whole record.

Report failures exit 5:

| Code | What to do |
|---|---|
| `REPORT_TOO_LARGE` | Nothing was delivered. Ask the user to narrow it: a shorter period, one branch, fewer accounts. |
| `REPORT_FORMAT_MISMATCH` | The PDF could not be produced. Say so. Do not deliver HTML instead. |
| `REPORT_NOT_PRODUCED` | The ERP returned no report. Check the parameters with the user before you run it again. |
| `REPORT_DELIVERY_FAILED` | The report ran but could not be prepared for download. |

Each report error says what happened in the ERP. If the batch wrote, that write stands: run only the report again. Analytical (ARM) reports are not supported: say they run from the Acumatica UI, and offer the figures as a query. Never report one as empty.

## Attachments

`--attach PATH` stages a file and attaches it to the record the batch leaves loaded: one `--attach` per file, all on the same record. `--attach-comment TEXT` right after a file's `--attach` stores a comment with that file. `--attach-token TOKEN` attaches a file `velixo files stage` already staged, by the token it printed: see `velixo guide files`.

- Attach only a file the user gave you. Never one you generated, re-encoded or guessed. If you have no file, say you cannot attach one.
- The batch must create or load the record the file goes on. Every file in one run lands on that one record; two records take two runs.
- Add a comment only from what the user said or the document says ("Vendor quote 4471, emailed 14 Aug 2026"). Never invent a date, a reference or a sender.
- Limits: 5 files, 5 MB per file, 20 MB per run, 255 characters of comment, and an allowed list of extensions (PDF, images, text and data, Office, OpenDocument, mail; no archives). Relay which file was refused and why. Never rename a file to get past the list.

| Code | Exit | What to do |
|---|---|---|
| `ATTACHMENT_RECORD_WRITTEN_FILE_NOT_ATTACHED` | 5 | The record is saved; only the files failed. **Do not run the batch again**: it would create a second record. Build a batch that only loads that record by its saved key (no new record, no save), and run it with `--attach` for the files the error names. |
| `ATTACHMENT_UPLOAD_FAILED` | 5 | Nothing was saved. The same command is safe to run again. |
| `ATTACHMENT_TOKEN_INVALID`, `ATTACHMENT_INTEGRITY_MISMATCH` | 5 | The staged file was refused. With `--attach`, run again: it stages a fresh copy. With `--attach-token`, stage the file again with `velixo files stage` and pass the new token. |
| `ATTACHMENT_TOKEN_ALREADY_USED` | 5 | The file is already on a record. Nothing needs undoing. Read the record; never retry. |
| `ATTACHMENT_COMMAND_REJECTED` | 5 | The batch itself carried a file command. Nothing ran. Build again, and pass the file with `--attach`. |
| `ATTACHMENT_RECORD_NOT_LOADED` | 5 | The file read ran with no record loaded: a batch-ordering problem. Build again. |
| `ATTACHMENT_NOT_PERMITTED` | 5 | The ERP refuses this connection the record or its files. Name what was refused. Do not retry. |

## When it fails

| Exit | Code | Meaning | What to do |
|---|---|---|---|
| 6 | `ERP_ACTION_REFUSED` | The ERP refused an action because of the record's state: "The Release button is disabled.", "… is on hold", "… requires approval". | Do not run the batch again or rebuild it from the same request: it fails the same way. Run a read-only batch for the document's status, hold, credit hold, approval and released flags, and tell the user which one blocks it. |
| 6 | `TASK_FAILED` | The batch failed in the ERP. `error.detail` holds the ERP's text, then the screen's feedback. | A batch is not atomic: earlier commands may have saved. Read the record before anything else. Build again quoting the error. |
| 124, 130 | `TIMEOUT`, `INTERRUPTED` | The task keeps running. | Follow it with `velixo task status TASKID --wait`. Do not run the batch again. |

On a refused state:

- Hold flags differ. A credit hold is not the user hold: a document with Hold = false can still be blocked, and removing the hold does not clear a credit hold.
- Clearing a hold, a credit hold or an approval is the user's business decision. Never do it to get past a refusal.
- A `processStatus` of `failed` on a release or post is not by itself a state refusal: a closed period or a missing account fails the same way. Report what `detail` says.
- "The Save button is disabled." (`ERP_ACTION_REFUSED`) after a batch that only sets a note is not a failed write. On a locked record, such as a closed document, the note saves without the save. Read the note back; when it holds the new text, report it saved.

## Notes, approvals and reassignments

- **Notes.** When the build returned `readFirst`, run it first, with only the values it uses. It only reads, so it needs no question to the user. `command run` takes a file with `target` and `commands`, so copy both out unchanged:

  ```sh
  jq '{target: .result.data.target, commands: .result.data.readFirst.commands}' note.batch.json > note-read.batch.json
  ```

  If the note is empty, or the user said to replace it, run the write with their text. If it holds text and the user did not say, ask whether to replace it or add to it. To add, combine the texts yourself and pass them as the one note value. Run the write soon after the read, or read again first.
- **Approve, reject and reassign are done only when a read says so.** Acumatica can drop these without an error. Afterwards, run a separate read of the document's approval rows (assignee, workgroup, status) and its status. An approval or rejection is done only when the row's status changed; a reassignment, when the assignee changed. When the read names someone other than the person asked for, an active delegate holds it: say who. If nothing moved, say it did not happen; never run the batch again with a reformatted value.
- Reassign takes the new approver's contact ID, a number. Look it up from the employee ID or name with `velixo query build` and `velixo query run`.
- "You are not an authorized approver for this document." is a state refusal: report the assignee and the workgroup, and that the Acumatica user of this connection may not act on it. Never offer a reassignment as the way past it.

## Continuations (Acumatica)

Send the header, every row and the finish in one run when you can. Use a continuation only for more than 50 rows, the most one call takes, or to let the user review the header first.

```sh
velixo command run --batch je.batch.json --param Date=2026-09-30 --rows rows-1.json --leave-open --yes
velixo command run --batch je.batch.json --continue --rows rows-2.json --leave-open --yes
velixo command run --batch je.batch.json --continue --rows rows-3.json --finish --yes
```

- `--leave-open` leaves the record open. The output holds `result.continuation` and `result.continuationExpiresAt`, and the CLI keeps them for `--continue`. `--continuation FILE` continues from a saved output instead.
- `--continue` sends no header. `--param` then binds the finish set only. End with `--finish`; the output shows `result.continuation: null` and the saved key.
- Every call writes to the open record and needs `--yes`.
- The record stays open for 2 minutes after the last call. Send the chunks back to back. Any other `command run` in between, even a read, ends the open record: finish one record before you start the next.
- Do not use `--no-wait` with `--leave-open`: the CLI keeps the continuation only when it waits for the result.
- A result with `continuationUnavailable` means this screen cannot be continued safely: run the rest in one call with the header.
- A batch that moves to another screen (a reclassification that continues on a processing screen) runs whole in one call. It is refused with `--leave-open`, `--continue`, `--continuation` or `--attach`. To preview, run the preview batch, then the processing batch as a second complete run after the yes.

| Code | Exit | What to do |
|---|---|---|
| `NO_CONTINUATION` | 2 | Nothing is open on this profile. Start from the header with `--leave-open`. |
| `SCREEN_SESSION_LOST`, nothing ran | 5 | The session closed and unsaved lines are lost. Start again from the header; if the header was saved, load it by its key. |
| `SCREEN_SESSION_LOST`, part-way | 5 | Read the record before anything else. |
| `SCREEN_CONTINUATION_MISMATCH` | 5 | The session holds another record. Do not retry. Read what it holds, or start again. |
| `SCREEN_CONTINUATION_UNVERIFIABLE` | 5 | Start again from the header. |

## Other ERPs

- **Business Central** keeps no record open: `--leave-open` and `--continue` exit 5 with `BC_RECORD_SESSION_UNSUPPORTED`.
- **Sage Intacct** batches have no line sets and no open records: each runs whole in one call. Reports come back as PDF, CSV, Excel, HTML or text files.

Next: report the result in business words, with the key from `result.feedback.savedKey`. For a long task, `velixo guide task status`.
