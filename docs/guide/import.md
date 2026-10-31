<!-- commands: import scenarios, import run -->
# Imports: velixo import scenarios, velixo import run

An import scenario is the customer's own mapping from a spreadsheet to an Acumatica screen, saved in Acumatica. `velixo import scenarios` lists the scenarios and the columns each one reads. `velixo import run` runs one over a file, exactly as the customer defined it, and writes one or more records per row. Use it for a bulk write that a scenario covers. When none does, write record by record with `velixo command build` and `velixo command run`.

Never drive the Import Scenarios (SM206025) or Import by Scenario (SM206030) screens with `command build` or `command run`. They cannot run that way, and the rights error you get is false. These two commands are the only import path.

## Example

```sh
velixo import scenarios --connection gab-dev --screen AR303000
velixo import run --connection gab-dev --scenario "Import Customers" --file customers.csv --validate-only --intent "..."
velixo import run --connection gab-dev --scenario "Import Customers" --file customers.csv --yes --intent "..."
```

`--file-token TOKEN` imports a file `velixo files stage` staged, in place of `--file PATH`: see `velixo guide files`. A real run spends the token, so another run needs a new one.

Both commands need a connection that someone stated. Pass `--connection` every time. The CLI also accepts the default from `velixo connections switch`, but it never lets the server pick one for an import. Run `velixo connections list` if you do not know the name.

## Before the user builds a file

Run `velixo import scenarios` and tell the user the scenario's `columns`. They are the contract. Every one must appear in the file's header row, matched without case and ignoring spaces. A file that lacks one is refused whole, not part imported. Extra columns are fine.

`apiEnabled: false` means this connection's Acumatica user cannot drive the scenario's screen through the API, so `columns` is absent. That is a change on Access Rights by Screen (SM201020). Do not retry.

## Before a write

`import run` always stages the file and validates it first. Follow these steps:

1. Run with `--validate-only`. It writes nothing and prints the preview on stdout: `scenario`, `screenId`, `screenTitle`, `file` (`name`, `sheet`, `range`, `rows`), `columns`, `sample` and `warnings`.
2. Tell the user the scenario, the screen, the connection, the row count and every warning. Warnings say what was interpreted for them: a guessed CSV delimiter, dates converted from Excel numbers, a row padded to the header's width.
3. Say that the import is not atomic and has no undo.
4. Wait for an explicit yes.
5. Run again with `--yes`. The command validates again, then runs the import and follows it, with progress on stderr.

Without `--yes` and without a terminal, the command prints the preview and exits 2 with `CONFIRMATION_REQUIRED`. Nothing was imported.

Do not paste rows into the chat, from the file or from `sample`, to show or confirm them. If a column is wrong, name the column and let the user fix the file.

Leave `--break-on-error` off. Every row is then attempted and reported, which is what someone fixing a spreadsheet needs. Add it only when the user says a partial import is worse than none.

## What comes back

The report is in `result.data`: `totalRows`, `processed`, `failed`, `notAttempted`, `rows` (each with `row`, `processed`, `keys` and `error`), `failures` and `warnings`. A report with failed rows still exits 0, with a warning on stderr that counts them.

- `row` is the line of the user's file, the header being line 1. Use those numbers with the user.
- Say which rows landed and what they became: "rows 2 to 16 imported as CA000123 through CA000137".
- For a refused row, relay the ERP's own sentence in `error`. Do not guess a cause or merge different failures into one.
- `processed: false` and `keys: null` do not prove that nothing was written. Keys come back only for a row the ERP counted as processed. Say the ERP reported the row as failed and the record must be checked.
- Many "'X' cannot be empty" errors usually mean the row's key matched no record, so the scenario fell through to an insert. Tell the user the ID matched no record, and that the listed fields are what a new record would need, not columns missing from their file. Leave the informational sentences out of the cause.
- With `--break-on-error`, rows after the stop come back with `processed: false` and no error: they were not attempted. More than one row can still carry an error, and the halting row is not always the first one marked failed. The warning that the run stopped early names no row. Read the rows instead.

## After a partial failure

The import is not atomic. The ERP commits each row as it reaches it, so rows before a failure stay in the ledger. Read the ERP back before you report: build a query on the scenario's screen, filtered on records modified since the run started, with `velixo query build` and `velixo query run`. Compare it with the rows reported as processed. To check one line by its ID, ask the user for that ID. Do not guess one.

## When it fails

| Exit | Code | What to do |
|---|---|---|
| 2 | `USAGE` | No connection, or the file does not exist. Fix the command line. |
| 2 | `CONFIRMATION_REQUIRED`, `CONFIRMATION_DECLINED` | Nothing was imported. Show the preview, ask the user, run with `--yes`. |
| 5 | `IMPORT_COLUMNS_MISSING` | The header lacks a column the scenario reads. The message names it and lists what the file has. |
| 5 | `IMPORT_DUPLICATE_HEADER`, `IMPORT_NO_ROWS`, `IMPORT_UNSUPPORTED_FILE` | Tell the user what to change in the file. |
| 5 | `IMPORT_SHEET_REQUIRED`, `IMPORT_SHEET_NOT_FOUND` | An Excel file needs `--sheet`. The second lists the sheets the workbook has. |
| 5 | `IMPORT_SCENARIO_NOT_FOUND`, `IMPORT_SCENARIO_SCREEN_UNAVAILABLE` | Check the name with `velixo import scenarios`. |
| 5 | `ATTACHMENT_TOKEN_ALREADY_USED` | The import already happened. Tell the user and read the ERP before anything else. |
| 6 | `TASK_FAILED` | The ERP failed the run. Part of it may have written: read the ERP back. |
| 124, 130 | `TIMEOUT`, `INTERRUPTED` | The import keeps running on the server. Follow it with `velixo task status TASKID --wait`. |

Every `IMPORT_*` code names what to change. None of them means the file was bad in a way the user cannot see.

## Rules

- Import only a file the user actually handed you.
- Each `import run` uploads the file again, and a real run spends its own upload. Running the same file twice creates everything twice when the scenario inserts, and reapplies every row when it updates.
- Never run an import again because it is slow, timed out or failed part way. A long import takes minutes. Pass `--no-wait` to get a task ID, and follow it with `velixo task status TASKID --wait`. Relay the progress it prints.
- Never report an import as done until the report shows it.

Next: `velixo import run --validate-only`, then `velixo import run --yes` once the user approves.
