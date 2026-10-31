<!-- commands: guide, config list, config get, config set, config path, tools list, tools guidance, help, version -->
# Setup and reference

## The guide

`velixo guide` prints the get-started page. `velixo guide COMMAND` prints a command's page, and `velixo guide GROUP` every page of a group, such as `velixo guide command`. The pages are part of the CLI, so they match its flags and need no sign-in. `--output json` prints `{ "sections": [{ "name", "commands", "markdown" }] }`.

## Help

`velixo help COMMAND`, or `--help` on any command, prints its flags and an example. `velixo help --all --output json` prints every command and flag as one JSON document, for an agent that needs the whole surface.

## Configuration

```sh
velixo config list                                 # the whole configuration
velixo config get defaultProfile
velixo config set defaultOutput json
velixo config set profiles.acme.environment nx-staging
velixo config path                                 # where the configuration, the sign-in and the state are kept
```

The keys are `defaultProfile`, `defaultOutput`, and `profiles.<name>.environment`, `profiles.<name>.url` and `profiles.<name>.connection`. `VELIXO_CONFIG_DIR` moves the folder. The sign-in lives in `credentials.json` beside it, readable only by you.

## The server's tools

Every command calls one of the Velixo server's tools. To see what the server offers, or to read its own rules for a tool:

```sh
velixo tools list --names
velixo tools guidance execute_commands
```

`tools guidance` prints the guidance the server writes for its other clients, in the server's terms. The pages of `velixo guide` hold the same rules in the CLI's terms; read those first.

## Version

`velixo version` prints the CLI's version.
