<!-- commands: connections list, connections switch -->
# Connections: velixo connections list, velixo connections switch

A connection is one ERP instance (an Acumatica tenant, a Sage Intacct company, a Business Central environment) that Velixo can reach for the signed-in user.

## List them

```sh
velixo connections list
velixo connections list --status ok
```

Each entry has a `name`, a `type` and a `status`, and, from a server that returns them, a `company` and a `url`. Only a connection with status `ok` can run commands; `no_credentials` means the user has not stored their ERP credentials in Velixo for it. The connection the commands use by default is marked `"default": true`, or `(default)` in a table.

## Link to a record

An app that creates or reads a record can link the user to it from `url` and `company`:

- Acumatica: `url` is the instance and `company` the tenant, so `{url}/Main?CompanyID={company}&ScreenId=EP301020&ClaimDetailCD=000123` opens expense receipt 000123.
- Business Central: `url` opens the environment, and `?company={company}&page=...` after it opens a page of that company.
- Sage Intacct: `url` is `null`, because every company uses the same host. `company` is the company ID.

Read them once with `velixo connections list --output json`, rather than asking the user to type them again.

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
