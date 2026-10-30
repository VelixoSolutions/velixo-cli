"""Runs the Velixo CLI and returns its JSON. Every ERP read and write in the sample goes through here."""

from __future__ import annotations

import json
import os
import subprocess
from dataclasses import dataclass
from pathlib import Path


class VelixoError(Exception):
    """The CLI failed. `code` is its error code, such as NOT_SIGNED_IN or TASK_FAILED."""

    def __init__(self, code: str, message: str, exit_code: int | None = None):
        super().__init__(message)
        self.code = code
        self.exit_code = exit_code


@dataclass
class Velixo:
    connection: str | None = None
    binary: str = "velixo"
    intent: str = "Create an expense receipt from a photo texted by an employee"
    timeout_seconds: int = 180

    def query(self, file: Path, parameters: dict[str, str] | None = None) -> list[dict]:
        """Runs a .vql file and returns every row as a dict keyed by column caption."""
        args = ["query", "run", "--query", str(file), "--all"]
        for label, value in (parameters or {}).items():
            args += ["--param", f"{label}={value}"]

        document = self._run(args)
        data = document["result"]["data"]
        captions = [column["caption"] for column in data["columns"]]
        return [dict(zip(captions, row)) for row in data["rows"]]

    def run_batch(
        self,
        file: Path,
        variables: dict[str, str | None],
        rows: list[dict[str, str | None]] | None = None,
        attachments: list[Path] | None = None,
        intent: str | None = None,
    ) -> dict:
        """Runs a saved batch with --yes and returns the result. A None variable is left at its default."""
        args = ["command", "run", "--batch", str(file), "--yes"]
        for name, value in variables.items():
            args += ["--param-null", name] if value is None else ["--param", f"{name}={value}"]

        rows_file = None
        if rows:
            rows_file = file.parent / f".rows-{os.getpid()}-{id(rows)}.json"
            rows_file.write_text(json.dumps([{"values": row} for row in rows]))
            args += ["--rows", str(rows_file)]

        for attachment in attachments or []:
            args += ["--attach", str(attachment)]

        try:
            return self._run(args, intent)
        finally:
            if rows_file:
                rows_file.unlink(missing_ok=True)

    def _run(self, args: list[str], intent: str | None = None) -> dict:
        full = [self.binary, *args, "--output", "json", "--quiet", "--non-interactive", "--intent", intent or self.intent]
        if self.connection:
            full += ["--connection", self.connection]

        try:
            completed = subprocess.run(full, capture_output=True, text=True, timeout=self.timeout_seconds)
        except FileNotFoundError as error:
            raise VelixoError("CLI_NOT_FOUND", f"The Velixo CLI was not found: {self.binary}") from error
        except subprocess.TimeoutExpired as error:
            raise VelixoError("TIMEOUT", "The Velixo CLI did not answer in time.") from error

        try:
            document = json.loads(completed.stdout)
        except json.JSONDecodeError as error:
            raise VelixoError("CLI_FAILED", completed.stderr.strip() or "The CLI printed no JSON.", completed.returncode) from error

        if "error" in document:
            failure = document["error"]
            raise VelixoError(failure.get("code", "CLI_FAILED"), failure.get("message", ""), failure.get("exitCode"))
        return document
