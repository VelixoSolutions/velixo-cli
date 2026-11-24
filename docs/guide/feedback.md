<!-- commands: feedback -->
# Feedback to Velixo: velixo feedback

`velixo feedback` tells Velixo R&D what kept a command from doing what the user needed, and the change you propose. It is how R&D learns why a command failed or had to be worked around.

## When to send one

Send one report per problem, as soon as you notice it and before your final answer, then carry on with the user's request. You do not need to ask the user first. Send one when:

- No command does what the user asked (`missing-capability`).
- A command lacks a filter, a column or a flag you needed (`missing-filter`).
- A page, a flag's help or a builder's answer misled you (`misleading-description`).
- A result you believe is wrong (`wrong-result`).
- An error that does not say what to change (`unclear-error`).
- A command that is too slow for the task (`slow`).

A builder that asks a question (exit 3) is not a problem to report: put the question to the user.

## Example

```sh
velixo feedback --tool "query run" --category missing-filter --severity worked-around \
  --attempted "The user wanted the open AR invoices of one branch from a saved inquiry." \
  --problem "query run --inquiry takes no filter, so it returned every branch." \
  --proposed-change "Add a --filter flag to query run --inquiry, such as --filter \"Branch=...\"." \
  --workaround "Built a new query with the branch as a parameter."
```

## Flags

| Flag | Meaning |
|---|---|
| `--tool COMMAND\|TOOL` | The command the problem is about, such as `"query run"`, or a server tool name. `none` when no command does it. The CLI sends a command as the tool it calls. |
| `--category CATEGORY` | `missing-capability`, `missing-filter`, `misleading-description`, `wrong-result`, `unclear-error`, `slow` or `other`. |
| `--severity LEVEL` | `blocked` (you could not do what the user asked), `worked-around` (you got there another way) or `minor`. |
| `--attempted TEXT` | What the user wanted, and what you tried, in a few sentences. |
| `--problem TEXT` | What the command did or lacked, with the error text when there was one. |
| `--proposed-change TEXT` | The change you propose, as concretely as you can: the flag, filter or wording to add, and what you would then have run. |
| `--workaround TEXT` | How you got the answer anyway. Leave it out when you did not. |

Every text holds at most 4,096 characters. Quote only the first lines of a long error.

## Describe the command, not the data

Name fields, filters, flags and screens. Leave out values from the rows, amounts, and the names of customers or vendors.

## Output

Prints `{ "result": { "text": "Thank you. Velixo R&D received the report (ID). Do not submit it again." } }`. When the caller has not joined Velixo's Quality Improvement Program, the server keeps only the tool, the category and the severity, not the text, and its answer says so. Either way, do not send the same report again.

## Errors

- A category or severity outside the lists, or an empty text, exits 2 before any call.
- A text over the limit exits 5 with the server's message. Shorten it and send again.
