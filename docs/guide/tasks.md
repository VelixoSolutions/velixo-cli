<!-- commands: task status -->
# velixo task status

Some work takes minutes: a slow build, a long batch, a large report, or an Acumatica process such as a release, a post or a shipment confirmation. Every command that starts such a task waits for it by itself and prints progress on stderr, for example `Release on SO301000, 2m14s, no change for 48s`. `velixo task status TASKID` collects a task you stopped waiting for.

## Example

```sh
velixo command run --batch release.batch.json --param RefNbr=INV001042 --yes --no-wait
# prints { "taskId": "t_8f3a...", "status": "running" }
velixo task status t_8f3a --wait > release.result.json
```

## Stop waiting, and come back

- `--no-wait` starts the task and returns after the first hold, with exit 0. It prints `{ "taskId", "status": "running" }`, or the finished result if the task was already done.
- `--timeout SECONDS` is one budget for the whole command. When it runs out, the command exits 124 with `error.taskId` and `nextStep`.
- Ctrl-C exits 130. The task keeps running on the server.
- `velixo task status TASKID` asks once and holds up to about 40 seconds. `--wait` follows the task to its end, with progress on stderr; `--timeout` still applies.

The final output has the shape of the command that started the task: a plan, query rows, or a `command run` result.

## When it fails

| Exit | Code | Meaning | What to do |
|---|---|---|---|
| 3 | `BUILDER_REFUSAL` | The build ended in a question. | Ask the user, then build again. |
| 5 | `SCREEN_*`, `REPORT_*`, `ATTACHMENT_*`, … | The server refused, with a code. | Read `error.code` and `nextStep`, and the command's own page. |
| 6 | `TASK_FAILED` | The task failed in the ERP. `error.detail` holds the ERP's text. | For a write, read the record before anything else. |
| 6 | `TASK_EXPIRED` | The server no longer knows the task: it was evicted, or started in another organization. | For a write, read the record to see whether it ran. |
| 124 | `TIMEOUT` | Your budget ran out, not the task. | `velixo task status TASKID --wait` |

## Rules

- **A write that timed out, was interrupted or ran with `--no-wait` is still running.** Never run the batch again to "retry" it: follow the task. If you lost the task id, read the record. The duplicate guard counts these runs, so the same batch is refused for 10 minutes.
- **Collecting a command run here skips what `velixo command run` does after the result.** It stores no continuation, so never use `--no-wait` with `--leave-open`. It downloads nothing: save the output and run `velixo files download --from FILE`. And an ERP refusal of an action ("The Release button is disabled.") comes back as `TASK_FAILED`: treat it as a refused state, as `velixo guide command run` says, and do not run the batch again.
- **A finished task is not a finished process.** A screen result can carry `result.processStatus` of `stillRunning`: the task is done, the Acumatica process is not. Read the document's status with a read-only batch.
- Tell the user a long task is still running, and what the progress line says.

A build collected with `task status` is not added to the profile's list. Redirect its output to a file and run it with `velixo query run --plan FILE` or `velixo command run --batch FILE`, with the `--connection` you built with.

Next: read the result as the starting command's page says, for example `velixo guide command run`.
