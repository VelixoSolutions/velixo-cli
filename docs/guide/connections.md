<!-- commands: connections list, connections switch -->
# Connections: velixo connections list, velixo connections switch

A connection is one ERP instance (an Acumatica tenant, a Sage Intacct company, a Business Central environment) that Velixo can reach for the signed-in user.

## List them

```sh
velixo connections list
velixo connections list --status ok
```

Each entry has a `name`, a `type` and a `status`. Only a connection with status `ok` can run commands; `no_credentials` means the user has not stored their ERP credentials in Velixo for it. The connection the commands use by default is marked `"default": true`, or `(default)` in a table.

## Which connection a command uses

The first of these that names one:

1. `--connection NAME` on the command.
2. `VELIXO_CONNECTION`.
3. `connection` in the project file `.velixo.json`.
4. The profile's default, set by `velixo connections switch`.
5. The server's choice, which it makes only when one connection fits. When several fit, the command fails and `nextStep` says to choose.

When several connections exist and the user did not say which, ask them. Never pick one yourself. A name the user gives always wins.

## Change the default

```sh
velixo connections switch            # on a terminal: pick with the arrow keys, Enter to select
velixo connections switch gab-dev    # by name, in a script or an agent
velixo connections switch --clear    # remove the default
```

`switch` checks the name against the server's list, so it needs a sign-in. Without a terminal it needs the name. The default belongs to the current profile; `--connection` still overrides it on any command.

Next: `velixo guide query build` or `velixo guide command build`.
