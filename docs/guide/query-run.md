<!-- commands: query run -->
# velixo query run

`velixo query run` runs a built plan, one query, or a saved inquiry against the ERP and prints the rows. It involves no AI and is free to repeat. Give it a number or name from the profile's list, or exactly one of `--plan`, `--query` and `--inquiry`.

## Example

```
velixo query run 3 --param "As Of Date=2026-09-30" --intent "Month-end AR review"
```

- A number or name runs that plan from the profile's list (see `velixo guide library`), on the connection it was built on.
- `--plan FILE` runs every item of a plan saved with `velixo query build --save`, in order. A file does not remember its connection: pass the `--connection` you built with.
- `--query FILE` runs one query from a text file, such as the `.vql` file `velixo query build --save ar.vql` wrote, or `-` for stdin.
- `--inquiry NAME` runs a saved generic inquiry as saved.

An agent is never prompted and never paged: it gets one window of `--top` rows, with `truncated` and `totalCount`, and reads more with `--skip` or `--all`. (At a terminal, a person is asked for missing values and scrolls through the rows with the arrow keys, under column headers that stay on screen.)

## Passing the values

Every value the user gave became a `{{Placeholder}}` at build time, and the query declares a parameter for it. Expect a parameterized query: it returns nothing useful until you pass the values. That is not a broken build.

- Pass each value with `--param "Label=Value"`. The key is the display label, the text after `AS` in the `DECLARE` line, not the declared name. `DECLARE CustNbr SCHEMA [...].CustomerID AS 'Customer ID'` takes `--param "Customer ID=ABARTENDE"`. A parameter declared with no `AS` takes its declared name.
- Take the labels from the plan's `note`, not from a guess.
- `--params FILE` reads a JSON object keyed by label, or `-` for stdin. A `--param` with the same key wins.
- The text splits at the first `=`, so a value may hold more of them.
- For a date range, pass the first and last day of the span.

Before the run, the CLI reads the `DECLARE` lines and warns on stderr:

- for a key that matches no parameter. Acumatica discards an unknown key without an error, so the query answers for no value, or for its default, and looks as if it worked. Read the warning and fix the label.
- for a `REQUIRED` parameter with no `DEFAULT` and no value. The query then matches nothing.

Never ignore these warnings. On Sage Intacct and Business Central, an unknown key on `--query` is refused, and the error lists the keys the query takes.

### Plans

In a plan, one set of values feeds every item, and each query gets only the keys it declares. An item that runs a saved inquiry takes the parameters the plan gives it. Each `{{Placeholder}}` in them takes the value passed under the placeholder's name, as in `--param "BranchID=HQ"`. A value passed under the inquiry parameter's own label replaces the plan's value. A parameter whose placeholder has no value is left out, so the inquiry uses its own default, and the CLI warns.

### Saved inquiries

`--inquiry NAME` sends the values unchecked: the CLI cannot see the inquiry's parameters. Leave out a parameter you have no value for, and the inquiry uses its default. Unlike a query, an inquiry refuses an unknown label or a missing required value, and the error names the valid or missing labels. Ask the user for a missing value. For an unknown label, build again with `velixo query build`.

## Re-run, do not rebuild

The same question for another customer, period or date range is the same query with new values. Run it again with a new `--param`. Do not build again: a rebuild costs a round trip and can compute the figure a second, equally legitimate way, leaving two answers on screen that disagree.

- Check the query text first. A value in a `DECLARE` is re-supplied with `--param`. A value written inline in the query is not, and the CLI warns that your key matches nothing. That case is a rebuild.
- Keep the same `--top` when you re-run, so the two answers stay comparable.
- When the plan held several queries, re-run all of them with the new value. A control total left from the old value no longer ties out.

## Row windows

- `--top N` sets the rows per call: 250 by default, 10,000 at most.
- `--skip N` skips rows before the window.
- `--all` reads every window from `--skip` until one comes back short, and merges them into one `rows` array with one `totalCount`. `--max-rows N` caps it at 10,000 by default.
- Page only a query that has an `ORDER BY`. Paging an unordered query is not stable: a row can appear in two windows or in none. The CLI warns when `--all` meets one.
- For a very large `totalCount`, offer an aggregate rebuild rather than paging everything down.
- A count-only question needs no rows. Run with `--top 1` and read `totalCount`, which the ERP reports whatever the window. Do not page, and do not rebuild it as an aggregate: an ungrouped count has no aggregate form.

## What comes back

For one query, `result.data` holds `columns`, `rows` and `totalCount`. `rows[i][j]` lines up with `columns[j]`. `totalCount` is the full match count, whatever the window.

For a plan, the output is `{ note, results: [{ purpose, result }] }`, one entry per item in the plan's order. Combine them as the `note` says: merge keys, tie-out, sign convention, top N, entities with no activity shown as zero. When you report a reconciliation, state the total you tied out to and the sign convention you used.

- A column with `allowedValues` lists its codes as `{ value, label }`. Use it to name a code: `A` is Active. It is not a whitelist. A row can hold a retired code, a comma-joined pair from a multi-select field, or free text, so never call a value invalid because the list omits it.
- `truncated: true` and a `warning` mean the ERP matched more rows than the call returned. The CLI repeats the warning on stderr. Never analyse or present those rows as the whole answer. Their absence is what makes a result complete: the last window of a paged walk carries neither.
- With `--output table`, a footer names the window: `rows 1 to 250 of 1,204; add --skip 250 or --all for the rest`. With `--output csv`, a plan of more than one item is refused.

### Empty and all-zero results

- Zero rows is a valid answer, but on a parameterized query first rule out a dropped value: a mis-keyed `--param` on Acumatica leaves exactly this result. Read the stderr warnings.
- On Sage Intacct, an empty result can carry `warnings`: an inner join matched nothing because its key is empty. Then the zero rows are not an answer. Never tell the user that nothing matches. Build again on the field the warning suggests, or say that the join could not match.
- Many rows whose amounts are all exactly `0.00` are a red flag. A period or ledger filter that matched nothing still returns one row per entity, and no error. Before you report zeros, build the same figure without the breakdown, or for periods that carry activity, and compare. Report zeros only when a second, differently shaped query agrees.

## Presenting results

- Show the rows as a clean Markdown table, state the total row count, and call out the key figures in a sentence: totals, variances, top movers.
- Show up to about 250 rows inline. Beyond that, always say "Showing N of M rows".
- Beyond 500 rows, also say the result is large and suggest two or three ways to narrow it, drawn from the question: a shorter date range, the top N by amount, one level of grouping fewer.
- Then offer the rest, and fetch it only when the user says yes. For 251 to 1,000 rows, page in 250-row windows with `--skip`. Above 1,000, narrow or aggregate rather than page. At any size above about 250, offer a workbook of the full result with `velixo query workbook`.
- Never render more than about 1,000 rows inline, even when asked. Page and name the window ("rows 251 to 500 of 1,204"), or aggregate.
- When every matched row is on screen, the answer ends there. Offer no next step unless the user asks.

## When it fails

| Exit | `error.code` | What to do |
|---|---|---|
| 2 | `USAGE` | A bad flag, or a plan or parameter file that is missing or not valid. Fix the command line. |
| 6 | `TASK_FAILED` | The ERP failed the query. `error.detail` holds its text. |
| 5 | `TOOL_ERROR` | The server refused the call. When several connections fit, run `velixo connections list`, ask the user, and pass `--connection`. |
| 124 | `TIMEOUT` | The query still runs. Collect it with `velixo task status TASKID --wait`. |

An HTTP 400 that carries a SQL conversion error ("Conversion failed when converting the nvarchar value '…' to data type int") means the built query is wrong, not your call. Build again with `velixo query build`, quoting the error verbatim. Never retry the same run. A connection or timeout error from the ERP is the opposite: retry the same run.

## Rules

- Run only what `velixo query build` built, or a ready-made query a skill or the user supplied as text, exactly as supplied. Never edit a query to fix it. If a ready-made query fails, report the error and stop, or build through `velixo query build` quoting the error.
- A follow-up that changes the shape of the result (another filter, sort, grouping or columns) is a rebuild with `--base-query`, not a run.

Next: present the rows. For the full result in Excel, `velixo query workbook --plan FILE`.

## Running a query inline

To run a query you hold as text without writing a file, pass it on stdin. In bash or zsh:

```
velixo query run --query - --param "As Of Date=2026-09-30" <<'VQL'
DECLARE [AsOf] SCHEMA [PX.Objects.AR.ARInvoice].DocDate AS 'As Of Date' REQUIRED
...
VQL
```

In PowerShell:

```
@'
DECLARE [AsOf] SCHEMA [PX.Objects.AR.ARInvoice].DocDate AS 'As Of Date' REQUIRED
...
'@ | velixo query run --query - --param "As Of Date=2026-09-30"
```
