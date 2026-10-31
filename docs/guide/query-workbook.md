<!-- commands: query workbook -->
# velixo query workbook

`velixo query workbook` turns a built query into a Velixo workbook the user opens in Excel. The workbook holds the query as a live formula, not its results: it fetches current figures from the ERP each time it is opened or refreshed, and the user can extend it into a report. The command runs nothing and returns no rows; use `velixo query run` when you need the figures yourself.

## When to use it

- Only when the user asks for Excel, a spreadsheet or a workbook.
- A result too large to show is not that request. Show what fits, offer a workbook as one way to get the rest, and create it only when the user says yes. Never create one unasked.

## Example

```
velixo query workbook --plan ar.plan.json --param "As Of Date=2026-09-30" \
  --download ar.xlsx --intent "AR balance per customer, in Excel"
```

- `--plan FILE` takes a plan saved from `velixo query build`. `--item N` picks the item, from 1. A plan with one item needs none; a plan with several needs it.
- `--query FILE` takes one query from a file, or `-` for stdin.
- A number or name from `velixo query list` makes a workbook from that plan. Give a number or name, or one of `--plan` and `--query`.
- `--param "Label=Value"` and `--params FILE` set the parameters, keyed by display label, as on `velixo query run`. The values are written into the workbook as editable cells, so the user can change them and refresh. Leave them out when the query declares none.
- `--download PATH` saves the workbook to that file, or into that directory under its own name. A file already in the directory is kept, and the new one is saved as `name (2).ext`.

The CLI reads the query's `DECLARE` lines and warns on stderr for a key that matches no parameter, and for a required parameter with no value. Fix the label and create the workbook again before you hand the file over.

## Rules

- Pass the query as built, `DECLARE` lines included. The workbook re-runs that text, so anything you change is what the user's report runs. Never edit it. To change it, build again with `velixo query build --base-query`.
- A plan item that runs a saved inquiry as saved carries no query, so it cannot become a workbook: the command exits 2. Build again and ask for that inquiry as a workbook ("the AR Aging GI as a workbook"). The builder then returns a query over the inquiry.
- A workbook holds one query. From a plan of several items, choose one with `--item`. The plan's `note` on how the items combine does not go into the workbook, so tell the user.
- The workbook opens on the same connection, so the user is not asked to sign in again.

## What comes back

```json
{
  "result": {
    "data": {
      "attachments": [
        { "fileName": "AR balance.xlsx", "sizeBytes": 18234, "downloadUrl": "https://app.velixo.com/mcp/download/...", "kind": "queryWorkbook", "path": "ar.xlsx" }
      ],
      "note": "..."
    }
  }
}
```

`path` appears only with `--download`. The link expires in about an hour. Without `--download`, fetch it with `velixo files download URL --out PATH`, or create the workbook again rather than send a stale link.

Give the user the file and name the format. Say what each parameter cell expects: the user fills them in next.

## When it fails

| Exit | `error.code` | What to do |
|---|---|---|
| 2 | `USAGE` | No query or plan, a missing `--item` on a plan of several items, or an inquiry item. Fix the command line, or build again as above. |
| 5 | `TOOL_ERROR` | The server could not make the workbook. "Could not be prepared just now" is safe to retry in a few seconds. "Cannot be turned into a workbook" means check the connection name with `velixo connections list`, and that the query is not enormous. |
| 4 | `NOT_SIGNED_IN` and others | Sign in again, or relay the reason. |

Next: hand the file to the user. To see the figures yourself, `velixo query run --plan FILE`.
