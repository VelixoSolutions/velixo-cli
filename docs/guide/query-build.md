<!-- commands: query build -->
# velixo query build

`velixo query build` turns a data question in plain words into a query plan, validated against the connection's real schema. It is the only source of query text: use it for every data question, and also when the user wants the query itself ("give me the query for …", "make that a parameterized query"). The build takes 10 to 60 seconds and prints the plan on stdout.

## Example

```
velixo query build "Outstanding AR balance per customer as of {{AsOfDate}}" \
  --intent "Month-end AR review" --save ar.plan.json
```

The build keeps the plan in the profile's numbered list: `velixo query run 3` runs it and `velixo query workbook 3` turns it into a workbook. Save it to a file as well (`--save ar.vql` for one query) when you will refine it, because `--base-query` takes a file.

## Writing the request

Write every value the user gave you as a `{{Placeholder}}`, never the value itself. "Outstanding AR balance for customer `{{CustomerID}}`", not "… for customer ABARTENDE". Settle this for every value before you write the request: it is the rule callers most often get wrong.

- The rule covers every value the user chose, whatever field it filters. The examples here show the syntax; they are not the list of fields it applies to.
- What you chose yourself stays in words: a status code, the ledger, released-only.
- Name each placeholder for what it holds: `{{CustomerID}}`, `{{DateFrom}}`, `{{DateTo}}`. Two values of the same kind are two names.
- A list is one placeholder per member: "in ledgers `{{Ledger1}}`, `{{Ledger2}}` or `{{Ledger3}}`", never one `{{Ledgers}}`.
- A time span is a date range unless the user asked for a period. "December 2021", "Q4 2021" and "2021" are `{{DateFrom}}` and `{{DateTo}}`, and at run time you pass the first and last day. Write `{{FinPeriod}}` or `{{PeriodFrom}}`/`{{PeriodTo}}`, and the words "financial period", only when the user said "period", "fiscal period" or "posting period". "12-2021" without the word "period" is still a date range.
- Never split one value between words and a placeholder. "December `{{Year}}`" drops the month from the query. The whole span is the value, so the whole span is the placeholder.
- Row count and paging are not placeholders: they are `--top` and `--skip` on `velixo query run`. Sorting is part of the query: name the column and the direction in words.
- Otherwise write the need in natural language: filters, sorting, grouping, period and currency context.

The values stay with you until `velixo query run`. They never enter the build, so the same question gives the same query for every caller, and one build answers for every value.

### Saved inquiries

A request that names a saved generic inquiry keeps the name in words, exactly as the user said it: "the AR Aging GI for branch `{{Branch}}`", "inquiry GI000123 as of `{{AsOfDate}}`". The builder finds the inquiry on the instance and runs it as saved. Do not ask which inquiry first, and do not rebuild it from tables yourself. Its parameter values are placeholders like any other value.

A direct run of an inquiry carries no query text, so it cannot become a workbook or a formula. When the user wants the inquiry as a workbook, a formula or a query to save, say so in the request ("the AR Aging GI as a workbook"). The builder then returns a query over the inquiry.

### Analyses and drill-downs

"Analyze", "break down" and "explain" a figure are drill-downs, and a drill-down needs a grouping axis. On Acumatica the axes are Company, Branch, one of possibly several Subaccount segments, and business account, and several can be combined. If the user named none, ask which they want. Never pick an axis yourself.

Before you build, ask in one message for every missing input: the filters, the entity, the period, the grouping axis. Never invent a customer, vendor, project or other identifier.

## What comes back

```json
{
  "result": {
    "data": {
      "queries": [
        { "purpose": "Outstanding AR balance per customer", "query": "DECLARE AsOf SCHEMA [...] AS 'As Of Date' ... SELECT ..." }
      ],
      "note": "Pass 'As Of Date' as YYYY-MM-DD. Natural Net is credit less debit."
    }
  },
  "libraryNumber": 3
}
```

- `queries` holds one or more items. A simple question gives one. An analysis (actual against budget, a segment breakdown, a top N over an aggregate) gives several on purpose.
- Each item holds a `query` in VQL, or `inquiry: { name, parameters }` for a saved inquiry run as saved.
- `note` says how to combine the items (merge keys, tie-out, sign convention) and names each parameter's label and the value it expects. Read it before the first run.
- The query declares one parameter per placeholder in its `DECLARE` lines. Its label is the text after `AS`, and that label is what `--param` takes.

When the user asked for the query itself, show it as built, unedited, and say what each parameter expects: the user fills them in next.

## When it fails

| Exit | `error.code` | What to do |
|---|---|---|
| 3 | `BUILDER_REFUSAL` | The builder needs something. `error.message` holds its `ERROR:` text. See below. |
| 5 | `TOOL_ERROR` | The server refused the call. When several connections fit, run `velixo connections list`, ask the user, and pass `--connection`. |
| 4 | `NOT_SIGNED_IN` and others | Sign in again, or relay the reason. |
| 124 | `TIMEOUT` | The build still runs. Collect it with `velixo task status TASKID --wait`. |

A refusal is not a failure to retry. Read what it asks:

- **A decision or a value for the user** (a grouping axis, a missing customer or branch, which of several inquiries, no inquiry matched). Put the question to the user in plain words, never the raw `ERROR:` text. Offer the options as a short list, one per line, with parallel labels. Build again with the answer as a `{{Placeholder}}`. Never guess a value or an axis to get past it.
- **A placeholder that cannot carry the query's shape** (a per-year comparison, a whole list behind one placeholder, a period inside a join condition). This one is addressed to you. Build again with that one value spelled out and every other value still a placeholder. Spell out the value and nothing else: do not repeat the builder's reasoning ("the year must be a literal"). The builder reads it as an instruction about the shape, and often splits a single grouped query into one query per period.
- **The user may not open the inquiry.** Report it to the user as a permission problem.

## Refining a query

A follow-up that changes the shape of a query rebuilds: another filter, a different sort, a new grouping, other columns, a member added to or removed from a list, or an inline value turned into a parameter. Write the full refined request, and pass the query you hold with `--base-query`:

```
jq -r '.result.data.queries[0].query' ar.plan.json > ar.vql
velixo query build "Outstanding AR balance per customer as of {{AsOfDate}}, largest first" \
  --base-query ar.vql --intent "Month-end AR review" --save ar-sorted.plan.json
```

- `--base-query` takes query text, not a plan: a `.vql` file, or `-` for stdin. Extract each item's `query` from the plan first.
- Repeat `--base-query` once per query when the plan held several, and pass them all.
- Never paste the query into the request. Only `--base-query` anchors the build.
- The builder starts from the base query and changes only what you asked, so every figure you did not change stays identical to the table on screen. Without it the build starts blank, and a figure that can be derived two legitimate ways may come back derived the other way.

A different value is not a refinement. Another customer, period or date range for a value the query declares is the same query, run again with another `--param`. Rebuild for a new value only when the query holds that value inline rather than in a `DECLARE`. The builder inlines a few on purpose: a per-year or per-ledger query, a list written out as values, a period inside a join condition. Check the `DECLARE` lines before you decide.

## Rules

- The builder is the only author. Never write a query from memory and never edit one, not even a one-word fix. A field that exists but means something else returns plausible wrong figures with no error.
- The one exception is a ready-made query that a skill or the user supplies as text. Run it exactly as supplied with `velixo query run --query`, and never refine it by editing.
- An ERP error on a built query means the query is wrong. Build again and quote the error verbatim in the request.

Next: `velixo query run --plan FILE --param "Label=Value"`.
