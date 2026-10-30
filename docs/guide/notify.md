<!-- commands: notify -->
# Notifications: velixo notify

`velixo notify` tells a person or a Teams channel something. It is the only way to reach somebody from the CLI, and how a scheduled task reports what it found. Velixo decides which surfaces a notification lands on, from where the recipient can be reached. There is no flag to pick email or Teams.

## Example

```sh
velixo query run --query ar.vql --param "As Of Date=2026-09-30" --intent "..." > ar.json
velixo notify --subject "AR aging ready" --body "12 customers are over 90 days." \
  --payload ar.json --source-name "Weekly AR check" \
  --link "https://..." --link-label "Open in Acumatica" --intent "..."
```

## Who it reaches

`--target` says what it is by its shape:

- **An email address** names a person. They are reached wherever they can be: the portal bell, email, and Teams or Slack where they have it.
- **A Teams channel link** (Teams, then the channel, then Copy link to channel) posts to that channel. It does not notify the channel's members one by one.
- **Several targets**, comma-separated, mix both: `"anna@velixo.com, https://teams.microsoft.com/l/channel/19%3A..."`. A person named twice gets one notification.
- **No `--target`** reaches the signed-in user. This is the common case for a task reporting back.

You can reach yourself and anyone who shares an organization with you. Any other address is refused, and one refused address does not stop the others.

## Before you send

The CLI asks for no confirmation: the notification goes out when you run the command. A message to anyone other than the user reaches a real person and cannot be taken back. Send it only when the user asked for it, to the people they named.

## What to put in it

- `--body` says what happened, in a sentence or two.
- `--payload` carries the data: a saved `velixo query run --query` output, passed unchanged. A plan of one query prints the same document. A plan of more than one prints `{ note, results }`: pass one item with `jq '.results[0].result' run.json > ar.json`. Do not render the rows as a Markdown table in `--body`. Each surface draws the payload to fit and says how much it shows: a Teams card the first 20 rows and 6 columns, an email 25 rows and 8 columns. A Teams card draws no table from the body, and a long one can break the Teams size limit.
- `--source-name` every time. Name the task or the agent as the recipient should see it. A recipient who cannot tell an automated message from a colleague's trusts neither. The default is `velixo-cli`.
- `--link` whenever one exists, because every surface truncates. Name it with `--link-label` after where it goes: "Open in Acumatica" for an ERP screen, "View the report" for a report. A label that names the wrong system is worse than none. A download link expires in about an hour, so it is a poor link for a message read later.
- `--severity` is `Info`, `Warning` or `Error`, and sets how loudly the notification shows.

## What comes back

The output is `accepted`, `reachedNobody`, `recipients` (each with `name` and `channels`, the surfaces that carried it and what became of each) and `note`.

- `accepted: true` means the target resolved and the notification is recorded. It does not mean a person saw it.
- `accepted: false` means the target matched nobody you are allowed to notify.
- `reachedNobody: true` means nothing pushed it: it waits in the portal until the recipient opens it. The CLI prints a warning on stderr. Do not report the recipient as notified. Say where it is waiting, above all for a result someone expects.

## When it fails

| Exit | Code | What to do |
|---|---|---|
| 2 | `USAGE` | `--subject` or `--body` is missing, or `--payload` is not a saved `query run` output. Fix the command line. |
| 5 | `TOOL_ERROR` | Read `error.message`. "Nothing was delivered" means nobody was reached and you may send again. |
| 1, 124 | `NETWORK_ERROR`, `TIMEOUT` | The outcome is not confirmed, so the notification may already have gone out. Do not send it again unless the recipient says they never got it. |

The same holds for any failure whose message does not say that nothing was delivered.

Next: tell the user who was reached and on which surface, from `recipients`.
