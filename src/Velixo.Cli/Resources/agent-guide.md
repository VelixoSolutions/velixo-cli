# Velixo CLI: guide for agents

The Velixo CLI runs the tools of the Velixo MCP server as shell commands. Every command makes the
same tool call an MCP host would make, so the server's rules apply unchanged: read them with
`velixo guide get_started` and `velixo guide <tool or command>`.

## Before the first command

1. `velixo status` checks the server, the sign-in and the connections. Exit 4 means sign in.
2. `velixo login` signs in. In an agent it prints `{ status: "authorization_required", browserUrl, nextStep }`:
   show the user the URL, then run the `nextStep` command with the address the browser landed on.
3. `velixo connections list` names the connections every other command takes as `--connection`.

## Commands and the tools they call

| Command | Tool |
|---|---|
| `velixo connections list` | `list_connections` |
| `velixo guide` | `tool_guidance` |
| `velixo query build`, `query run`, `query workbook` | `prepare_query`, `execute_query`, `create_workbook_from_query` |
| `velixo screen build`, `screen run` | `prepare_commands`, `execute_commands` |
| `velixo task status` | `get_status` |
| `velixo files upload`, `files request`, `files staged` | `get_upload_url`, `list_staged_files` |
| `velixo import scenarios`, `import run` | `list_import_scenarios`, `execute_import_scenario` |
| `velixo notify` | `send_notification` |
| `velixo call TOOL --args JSON` | any tool |

Every command that does work in the ERP needs `--intent "<the user's goal>"` or `VELIXO_INTENT`.

## Output

stdout holds one JSON document when stdout is not a terminal or an agent is detected; progress and
warnings go to stderr. A tool's `textualResponse` is parsed into `result.data`, or kept as
`result.text` when it is not JSON.

## Exit codes

| Code | Meaning | What to do |
|---|---|---|
| 0 | Success | Continue; read `warnings` and `truncated`. |
| 1 | Network, server or CLI failure | Retry once, then report. |
| 2 | Usage: bad flag, missing `--intent`, a confirmation needed | Fix the command, or ask the user. |
| 3 | The builder asked a question (`BUILDER_REFUSAL`) | Ask the user in plain words, then build again. |
| 4 | Not signed in, session ended, or access denied | `velixo login`, or relay the reason. |
| 5 | The server refused with a code in `error.code` | Follow `error.nextStep`. |
| 6 | The task failed or expired | Read `error.detail`. |
| 124 | `--timeout` elapsed; the task still runs | `velixo task status <taskId> --wait` |
| 130 | Interrupted | The task still runs on the server. |

On any non-zero exit stdout holds `{ "error": { code, exitCode, message, taskId?, nextStep? } }`.

## Writes

A command that can change the ERP asks for confirmation, or needs `--yes`. Summarise the write for
the user in business language and get their yes before you add `--yes`.

## Continuations

`screen run --leave-open` keeps a record open for about two minutes; `screen run --continue` adds to
it from a later command. The MCP session id is stored per profile, so continue from the same profile
and do not run other screen commands in between.
