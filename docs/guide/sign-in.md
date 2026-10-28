<!-- commands: login, logout, whoami, status, auth token -->
# Sign in: velixo login, logout, whoami, status, auth token

Every command that works in the ERP needs a signed-in profile. A profile is an environment plus its sign-in; `prod` is the default, and `--env` or `--profile` picks another.

## Sign in

```sh
velixo login                    # prod, in the browser
velixo login --env nx-staging   # another environment
```

Without a browser (a server, a container, an agent's shell), sign in in two steps:

```sh
velixo login --no-browser                 # prints the URL to open
velixo login --complete "<code-or-url>"   # the code, or the URL the browser landed on
```

Without a terminal, `velixo login` does this by itself. It prints `{ status: "authorization_required", browserUrl, expiresAt, profile, environment, nextStep }`. Give the user `browserUrl`, and tell them the browser will end on a page that does not load: that is expected, and they copy that page's address and paste it back to you. Then run the `nextStep` command with what they pasted; it names the profile. The sign-in expires at `expiresAt`, 10 minutes after it starts.

## Check the setup

- `velixo status` checks the server, the sign-in, the tools and the connections. Exit 0 means you can work, 4 means you must sign in, 1 means something else is wrong. It prints its own document, not the error envelope: `signedIn`, `server`, `tools`, `connections` and `problems`, which says what is wrong.
- `velixo whoami` prints who the profile is signed in as. It reads the stored sign-in and makes no network call, so it cannot tell you whether the session still works. Use `status` for that.

## Sign out

- `velixo logout` ends the Velixo session and deletes the profile's tokens and state.
- `velixo logout --all` does it for every profile.

## Jobs that cannot sign in

`velixo auth token` prints the profile's refresh token, for `VELIXO_TOKEN` in a job. The gateway replaces a refresh token each time it is used, and refuses one that was already used: a second job that presents the same token ends the session for both. Give each job its own token, and never run two jobs on one token at the same time. A session lasts at most 30 days, after which someone must sign in again.

## When it fails

| Exit | What happened | What to do |
|---|---|---|
| 4 `NOT_SIGNED_IN` | The profile has no sign-in. | `velixo login` |
| 4 `SESSION_ENDED` | The session expired or was revoked. | `velixo login` |
| 4 `ACCESS_DENIED` | Signed in, but Velixo denied access: no access to the AI tools, no credits, no account. | Tell the user the reason in `message`; an administrator must fix it. |
| 4 `SIGN_IN_FAILED` | The sign-in itself failed. | Read `message`, then try `velixo login` again. |

Next: `velixo connections list`.
