# Jira Tickets in Branches, Commits and Pull Requests

Applies to every change in this repository: the CLI's source, tests, scripts, samples, the site, the package manifests and the docs. No `paths:` scope on purpose: every change gets a branch, commits and a pull request.

Jira's GitHub integration links a branch, a commit or a pull request to an issue only when its name carries the issue key. A change without the key shows up on no ticket, so nobody looking at the ticket can find the work, and nobody looking at the work can find why it was done.

Tickets for this repository live in the Jira project **RD** (R&D), keyed `RD-<number>`.

## The names

| What                     | Format                                          | Example                                                       |
| ------------------------ | ----------------------------------------------- | ------------------------------------------------------------- |
| Branch                   | `RD-<ticket>-<description>`                     | `RD-1234-site-top-links`                                      |
| Commit message           | `RD-<ticket> <Imperative description>`          | `RD-1234 Move the cli.velixo.com links to a bar at the top`   |
| Pull request title       | Same as the commit message                      | `RD-1234 Move the cli.velixo.com links to a bar at the top`   |
| Pull request description | Opens with the ticket key, linked to the ticket | `[RD-1234](https://velixo.atlassian.net/browse/RD-1234)`      |

- The description in a branch name is kebab-case and names what the change does.
- Every commit on the branch carries the key, not only the first one.
- Describe the semantic essence of the change in each commit message. When a commit answers review feedback, say what changed, for example `RD-1234 Link the README's Documentation section (per PR feedback)`, never `Fix PR comments`.
- The pull request description's first line is the linked key alone, so a reviewer reaches the ticket in one click.
- Commits that a release workflow makes itself, such as the Homebrew formula bump on `main`, keep their own form.

## Getting the ticket

- **Never invent a ticket key**, and never reuse the key of an unrelated ticket to get a link.
- When the user gives no ticket, ask for its key before creating the branch. If the user has no ticket yet, ask them to create one in RD (or offer to create it there), or to say explicitly that this change goes without one.
- A change the user explicitly said goes without a ticket keeps the same forms without the key: `<description>` for the branch, `<Imperative description>` for commits and the title, and no ticket line in the description.

## Never push a `claude/` branch

Some tooling starts a session on a branch named like `claude/youthful-cray-syafd3`. Such a branch has no ticket key, so Jira links it to nothing. Never push it to the remote. Create the `RD-<ticket>-<description>` branch from the same commit and push that one instead, even when the session was started on the `claude/` branch.
