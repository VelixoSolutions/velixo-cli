<!-- commands: query list, query delete, command list, command delete -->
# The list of built queries and batches: velixo query list, command list

Every `velixo query build` and `velixo command build` keeps what it built in a numbered list on the profile, so you can run it again without a file. The build prints the number: `Saved as query 3`. In JSON the build's output carries it as `libraryNumber`.

Give it a name with `--name` to run it by that name instead: `velixo query build "AR aging as of {{AsOfDate}}" --name ar-aging`, then `velixo query run ar-aging`. A name is a key: letters, digits, `-` and `_`, starting with a letter, with no spaces. Case does not matter when you use it. A name names one entry of its kind: building again with the same name moves the name to the new build, and the old entry keeps its number. The output carries the name as `libraryName`.

## Run one again

```sh
velixo query run 3                                  # asks for each value at a terminal
velixo query run ar-aging --param "As Of Date=2026-09-30"
velixo query run 3 --param "As Of Date=2026-09-30"
velixo query workbook 3 --download ar.xlsx
velixo command run 2 --param CustomerID=ACME --rows rows.json --dry-run
velixo command run 2                                 # asks for each value and line, then asks before it writes
```

At a terminal, `velixo query run` or `velixo command run` with no number and no file shows the list to pick from with the arrow keys. A script or an agent passes the number.

An entry remembers the connection it was built on, and a run uses it. `--connection` still wins.

## List and delete

```sh
velixo query list
velixo command list
velixo query delete 3
velixo query delete ar-aging
velixo command delete        # pick it from the list at a terminal
```

`list` prints each entry's `number`, its `name` when it has one, `title`, the `values` a run takes, its `connection` and when it was built. A number is never reused, so a deleted entry's number stays free.

The list belongs to the profile and lives in `library/<profile>.json` beside the configuration. `velixo logout` leaves it alone. It holds the plans and batches only: the values you pass at run time are never saved.

## When it fails

| Exit | What happened | What to do |
|---|---|---|
| 2 `USAGE` | There is no entry with that number or name, a `--name` is not a key, or the run named no entry and no file without a terminal. | Run `velixo query list` or `velixo command list` for the numbers. |
| 130 | The pick was cancelled. | Nothing ran. |

Next: `velixo guide query run` or `velixo guide command run`.
