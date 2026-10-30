<!-- commands: -->
# Calling the CLI from an app

This page is for a program that runs the CLI for its users: a web app, a bot, a scheduled job. The program reads every answer as JSON and never meets a prompt.

## Save the queries and batches with the app

Build each query and batch once, with `velixo query build --save` and `velixo command build --save`, and keep the files in the app's repository. The app then runs them with `velixo query run --query FILE` and `velixo command run --batch FILE`. A saved file always gives the same shape of result, it involves no AI, and it travels with the app to every machine that runs it. Run each file against a test company before the app uses it.

## Run every command the same way

```
velixo query run --query employees.vql --all --output json --non-interactive --quiet --intent "Expense receipts app"
```

- `--output json` prints one JSON document on stdout.
- `--non-interactive` makes a missing value an error, never a prompt.
- `--quiet` keeps the progress lines off stderr.
- `--intent` says why the app calls the ERP. Every call needs one.
- A write also needs `--yes`, or it stops with `CONFIRMATION_REQUIRED` and runs nothing.

## Read the answer

Parse stdout whatever the exit code. A success holds `result`. A failure holds `error`, with `code`, `message` and `exitCode`. Decide on `error.code`, and show `error.message` to a person.

`velixo query run --query FILE` prints the rows in `result.data`: `columns`, each with its `caption`, and `rows`, one array of values per row, in the order of the columns.

| `error.code` | Exit | What the app does |
|---|---|---|
| `NOT_SIGNED_IN`, `SESSION_ENDED`, `ACCESS_DENIED` | 4 | Nothing ran. The machine needs a new sign-in, or the account needs access. Tell an administrator; retrying does not help. |
| `TASK_FAILED`, `ERP_ACTION_REFUSED` | 6 | The ERP refused the task. `error.message` says why, for example an inactive item. Show it; sending the same values again fails again. |
| `SCREEN_*` | 5 | The batch and its values do not fit. Fix the app or the batch. |
| `DUPLICATE_RUN` | 2 | The same write ran in the last 10 minutes. Pass `--force` only when the user really means a second record. |
| Any other code, exit 1 | 1 | Unexpected: the network, the server. A read may be retried once. |
| `TIMEOUT` | 124 | The task still runs on the server. |

A query parameter on a selector field, such as an item ID, refuses a value that no record holds: the query fails with `TASK_FAILED`, and `error.message` says the value "is not a valid value for parameter". When the app looks a record up by that parameter, it reads this as "no such record", not as a broken query.

On a write, exit 1 and exit 124 mean the outcome is unknown: the record may be saved. Never run the write again blindly. Read the record first, by a value the app chose, such as a reference or a description, and create it only if the read finds nothing.

## Pass the values

- `--param "Label=Value"` binds one placeholder. `--params FILE` reads them all from a JSON object.
- `--param-null Label` leaves an optional field to the ERP's default. It drops every command that writes that placeholder, so a batch with a project field can create a record with no project.
- `--rows FILE` gives the lines of a batch that has them, as a JSON array with one entry per line:

```json
[ { "values": { "ExpenseType": "Meals", "Amount": "54.22" } } ]
```

  A line's `rows` holds its sub-lines. Pass `[]` for no lines.
- `--attach PATH` attaches a file to the record the batch creates, in the same run. Do not create the record first and attach in a second run.
- `--all` reads every row of a query. Give the query an `ORDER BY`: without one, the pages are not stable, so a row can be missed or read twice, and the CLI warns on stderr.

`velixo guide query run` and `velixo guide command run` have every flag.

## Run calls in parallel

An app that serves several users runs several CLI commands at once, on one sign-in. CLI versions after 0.3.1 renew the sign-in safely when they do. With 0.3.1 or earlier, two commands that renew the sign-in at the same moment can end the session.

## Samples

The CLI repository holds two apps built this way:

- `samples/inventorychecker`
- `samples/expensereceipts`, a Python app that creates expenses in Acumatica and Sage Intacct, with Docker files

To run the CLI in a container, see the README's "Running in a container".
