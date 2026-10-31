<!-- commands: command build -->
# velixo command build

`velixo command build "REQUEST"` turns one screen operation, written in the user's words, into a batch of screen commands: create, update or release a document, run an action, run a report, read fields on a screen, or list or download the files on a record. The builder finds the screen in this instance's catalog and validates the batch against the screen's real schema. Building runs nothing in the ERP. Run the batch with `velixo command run`.

## Example

```sh
velixo command build "Create an AR invoice for customer {{CustomerID}} dated {{DocDate}} for {{Amount}}, and release it" \
  --intent "Bill ABC Studios for the September retainer" --save invoice.batch.json
```

The build keeps the batch in the profile's list; run it by its number. The batch is a reusable template: run it again with other `--param` values for the next customer, period or amount. Add `--save FILE` when you need the file, for example to extract `readFirst`.

## Write the request

- **Every value the user gave is a `{{Placeholder}}`.** Write "for customer `{{CustomerID}}` for `{{Amount}}`", never "for ABCSTUDIOS for 1500.00". The rule covers every value the user chose, whatever field it sets. You pass the values later with `--param`.
- **One placeholder per value, named for what it holds.** In a document with a fixed number of lines, two lines that set the same field take two names: `{{Line1InventoryID}}`, `{{Line2InventoryID}}`.
- **What you or the builder choose stays a word.** A document type the builder picks, an action name, released-only, a ledger: write them in words.
- **What decides the batch's shape stays a word, even when the user chose it.** Spell out the report, the screen or the action: "run report AR631000 with its default parameters", never "run report `{{ReportID}}`". The builder refuses a placeholder there.
- **Never name a screen or a report from your own knowledge, and never ask the user which one to use.** Names differ per instance. Describe the goal ("the AR aging as of period `{{Period}}`") and let the builder search.
- **Name an existing record by its keys.** The builder cannot see what the user sees: "this order" or "the invoice on screen" identifies nothing. Write "the sales order with order type `{{OrderType}}` and order number `{{OrderNbr}}`", and take the values from the conversation or the page context when you run. A key value you were given is a placeholder, even a document type.
- **Name an invoice the way the user did.** "AR invoice" and "SO invoice" lead to different screens. Use either word only when the user said it. An AR prefix on the number does not make it an AR invoice: an invoice raised from a sales order carries one too.
- **A document with any number of lines is built once, for any number.** Ask for it that way: "a GL journal entry with any number of lines, each with `{{Account}}`, `{{DebitAmt}}` and `{{CreditAmt}}`". The same holds for removing an unknown number of lines. Give an exact line count only for a short document of a fixed shape.
- **Steps on one record that the user asked for together are one request.** "Create an invoice for `{{CustomerID}}` … and release it" is one build and one batch.
- **A report: name the format.** HTML by default, PDF only when the user asked for it. When the user named a report but no format, offer "Rendered report (HTML)" and "Rendered report (PDF)" as two choices, and keep HTML if they do not answer. Where a table in chat would answer just as well, offer it too, named for the data it shows.
- **Files.** The builder never sees a file. For a file to attach, make the request create or load the record the file goes on; the file itself goes with `command run --attach`. To read files, ask to list or download the files on the record, with a name filter such as "the PDF files" when the user gave one.
- **A note on an existing record.** Writing a note replaces the whole note. Say in the request whether to replace the existing text. If you do not say, the build also returns `readFirst` (see below). When the record is being created, or the user said to replace, say so, and no `readFirst` comes back.

## What comes back

`result.data` holds the batch, and `libraryNumber` beside `result` its number in the profile's list, for `velixo command run`:

- `target`: the screen, or on Sage Intacct the object or report.
- `commands`: the batch, or its header when it has line sets. `lineCommands`, `splitCommands` (rows under each line) and `finishCommands` appear for a document with any number of lines.
- `variables`: each placeholder with its name, type and description. It is `[]` when the batch needs none. Bind each name with `--param`, `--param-null` or `--params`, or per row with `--rows`.
- `keyNumbering`: how the record's ID gets its value on this connection. `autonumbered: true` means Acumatica assigns the ID when it saves; `autonumbered: false` means the user's ID becomes the record's ID. `byDocType` gives the answer per document type. It is configuration: never carry it from one connection to another.
- `warning`, in two cases. The batch names a field the screen does not list, so the run will most likely refuse it: tell the user before they confirm, and build again without that value if they agree. Or the batch does not use a placeholder you named, because the builder read it as context: leave it out of `--param`, and if the user meant it to be written, tell them and build again.
- `readFirst`: a read-only batch that gets the current note. See the note rule in `velixo guide command run`.

Run it by its number, or save it with `--save FILE` to keep the file. Never edit a batch by hand, not even one word, and never write one yourself.

## When the builder refuses

The command exits 3 with `BUILDER_REFUSAL`, and `error.message` holds the builder's question. Never show it raw. Most refusals are questions for the user; some are addressed to you:

| The builder asks for | Who answers | What to do |
|---|---|---|
| A choice between candidate screens or reports | The user | Put the candidates it named to the user as choices, then build again with their pick. |
| A business identifier (customer, branch, warehouse, project, employee, location) | The user | Ask. Never invent one. |
| The keys of the record | You, when the conversation holds them | Build again naming the record by its key placeholders. |
| A grid row index | You | Derive it (below). The user cannot know it. |
| An invoice's origin module and released flag | You | Look them up with `velixo query build` and `velixo query run`, on the table the refusal names, filtered on the document type and reference number as placeholders. Build again saying in words what came back: "the unreleased invoice `{{RefNbr}}` raised from a sales order". Never guess, and never build against a screen the refusal said cannot load it. |
| A value spelled out instead of a placeholder | You | Send the same request with that one value written out and nothing else changed. Do not add the builder's reasoning to the request. |
| Leave to use the defaults ("supply the branch … or authorize their defaults") | The user | Ask whether the ERP may fill the fields the request does not name. On a yes, build again with the same request and `--accept-defaults`. |

A question can end with `(target for the rebuild: TARGET)`. Leave that note out when you ask the user, and build again with `--target TARGET`.

"Use the default" is not a missing value. When the user said it for every field they did not name, build with `--accept-defaults`: the builder then stops asking for a branch, a project or other fields the screen can default. For one field the batch already writes, leave it to Acumatica with `command run --param-null`. Either way, read back what the ERP resolved it to.

## Grid row index

A row in a pop-up panel is chosen by a 0-based index over the grid's key fields in ascending order. That is not the order a read returns the rows in, and a wrong index fails quietly: a real row is chosen and the document saves with a line nobody named. A processing grid needs no index: name each document by its keys.

1. Build and run a separate read-only batch that reads the grid's key columns and the column that shows what a row still has left (open quantity, unclaimed amount).
2. Sort the rows yourself by the fields the refusal named.
3. Skip the rows with nothing left. Selecting one adds a zero-quantity line.
4. Take the position in that order, and build again with it.

If the refusal does not name the fields that order the grid, build again quoting the refusal word for word and ask which fields the index runs over. Never guess, never use the read's row order, never ask the user, and never reuse an index with other `--param` values: the same number in another vendor's grid is another row.

## Refine a batch

- **A different value is not a rebuild.** Run the same file with other `--param` values.
- **A different operation rebuilds**: other fields, another screen, another action. Send the full refined request, and pass `--target` with the `target` of the batch you hold so the builder starts on the same screen. Never paste the batch into the request: the request loses its angle brackets, so `<NEW>`, the literal that creates a new record, vanishes and a create edits whatever record the screen was on.
- **After a failed run**, build again with the ERP's error text quoted word for word in the request. The builder uses it to tell a malformed batch from a record whose state forbids the action, and answers the latter with a read of the record's status.

## Sage Intacct

A batch targets a REST object (create, update or delete a record, attach or read its files) or a report, which comes back as a PDF, CSV, Excel, HTML or text file. There are no line sets and no open records: every batch runs whole in one `command run`.

Next: `velixo command run --batch FILE --dry-run`, then `velixo guide command run`.
