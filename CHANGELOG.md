# Changelog

All notable changes to the Velixo CLI are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the version numbers follow [Semantic Versioning](https://semver.org/).

## [0.1.0] - Unreleased

First version. The CLI is one binary named `velixo` for Linux, macOS and Windows. It calls the Velixo MCP server and exposes its 14 tools as commands.

### Added

- Sign in and status: `velixo login` (browser sign-in with PKCE on a loopback redirect, `--no-browser`, `--complete`), `logout`, `whoami`, `status` and `auth token`. `VELIXO_TOKEN` for jobs that cannot sign in.
- Environments and profiles: `--env` (`prod`, `nx-staging`, `nx-stage-zero`, `nx-dev`, `local`), `--url`, `--profile`, `config.json` and `.velixo.json`.
- Connections and guidance: `connections list`, `guide` and `guide --cli`.
- Queries: `query build`, `query run` (`--plan`, `--query`, `--inquiry`, `--param`, `--params`, `--top`, `--skip`, `--all`, `--max-rows`) and `query workbook`.
- Screen commands on Acumatica: `screen build` and `screen run` (`--var`, `--var-null`, `--vars`, `--rows`, `--attach`, `--dry-run`, `--yes`, `--force`, `--download-dir`), with the write confirmation and the duplicate guard.
- Continuations: `--leave-open`, `--continue`, `--continuation` and `--finish`, with local checks of the session and the deadline.
- Tasks: `task status` and `--no-wait` on every command that starts a task.
- Files: `files upload`, `files request`, `files staged` and `files download`.
- Import scenarios on Acumatica: `import scenarios` and `import run` (`--validate-only`, `--break-on-error`).
- Notifications: `notify`.
- Raw access and discovery: `tools list`, `call` and `help --all` (`--output json` prints the whole command tree).
- Configuration: `config get`, `config set`, `config list`, `config path` and `version` (`--check`).
- Output formats `json`, `table` and `csv`, the documented exit codes (0, 1, 2, 3, 4, 5, 6, 124, 130) and the JSON error document.
- Agent detection from environment variables, sent to the server for telemetry. The CLI sends no telemetry of its own.
- `velixo version --check`, which reads the latest release from GitHub.
- Binaries for `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64` and `linux-musl-x64`, the install scripts `install.sh` and `install.ps1`, and `checksums.txt` on every GitHub Release.

### Not in this version

- Sign-in with a device code (planned).
- `velixo agent setup` (planned).
- Package managers: Homebrew, Scoop, WinGet, apt, yum and the Docker image (planned).

[0.1.0]: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v0.1.0
