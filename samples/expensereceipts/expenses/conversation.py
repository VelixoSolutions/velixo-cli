"""The text conversation with one employee: a photo comes in, at most a few questions go out, and the reply
ends with a link to the expense created in the ERP."""

from __future__ import annotations

import difflib
import json
import logging
import os
import re
import threading
import uuid
from dataclasses import asdict, dataclass, field
from datetime import date
from pathlib import Path
from typing import Callable

from .erp import Choice, CreatedExpense, Erp, Expense
from .reader import ReceiptNotRead, ReceiptReader, ReceiptReading
from .scan import straighten_receipt
from .velixo import VelixoError

Reply = Callable[[str], None]

MAX_OPTIONS = 9


@dataclass
class Draft:
    """A receipt on its way to the ERP, kept between messages while a question waits for its answer."""

    receipt_path: str
    notes: str
    employee_id: str | None = None
    reading: dict | None = None
    asking: str | None = None
    options: list[str] = field(default_factory=list)


class Store:
    """Keeps each phone's waiting receipt, and the employee each unknown phone said it belongs to."""

    def __init__(self, folder: Path):
        self.folder = folder
        self.folder.mkdir(parents=True, exist_ok=True)
        self._file = folder / "conversations.json"
        self._lock = threading.Lock()

    def draft(self, phone: str) -> Draft | None:
        with self._lock:
            data = self._load()["drafts"].get(phone)
        return Draft(**data) if data else None

    def save_draft(self, phone: str, draft: Draft | None) -> None:
        with self._lock:
            data = self._load()
            if draft is None:
                data["drafts"].pop(phone, None)
            else:
                data["drafts"][phone] = asdict(draft)
            self._save(data)

    def known_employee(self, phone: str) -> str | None:
        with self._lock:
            return self._load()["phones"].get(phone)

    def remember_employee(self, phone: str, employee_id: str) -> None:
        with self._lock:
            data = self._load()
            data["phones"][phone] = employee_id
            self._save(data)

    def save_receipt(self, jpeg: bytes) -> Path:
        path = self.folder / "receipts" / f"receipt-{uuid.uuid4().hex[:12]}.jpg"
        path.parent.mkdir(exist_ok=True)
        path.write_bytes(jpeg)
        return path

    def _load(self) -> dict:
        if not self._file.exists():
            return {"drafts": {}, "phones": {}}
        return json.loads(self._file.read_text())

    def _save(self, data: dict) -> None:
        temporary = self._file.with_suffix(".tmp")
        temporary.write_text(json.dumps(data, indent=2))
        os.replace(temporary, self._file)


class Conversation:
    def __init__(self, erp: Erp, reader: ReceiptReader, store: Store, base_currency: str = "USD", today: Callable[[], date] = date.today):
        self.erp = erp
        self.reader = reader
        self.store = store
        self.base_currency = base_currency.upper()
        self.today = today
        self._locks: dict[str, threading.Lock] = {}

    def handle(self, phone: str, text: str, photos: list[bytes], reply: Reply) -> None:
        """Handles one incoming message. Messages from one phone are handled one at a time, in order."""
        with self._locks.setdefault(phone, threading.Lock()):
            try:
                self._handle(phone, text.strip(), photos, reply)
            except VelixoError as error:
                self.store.save_draft(phone, None)
                reply(_velixo_failure(error))
            except Exception:
                logging.exception("Could not handle a message")
                self.store.save_draft(phone, None)
                reply("Something went wrong with that receipt. Please send the photo again.")

    def _handle(self, phone: str, text: str, photos: list[bytes], reply: Reply) -> None:
        if photos:
            try:
                receipt = straighten_receipt(photos[0])
            except ValueError:
                reply("I couldn't open that photo. Please send the receipt as a JPEG or PNG picture.")
                return
            draft = Draft(receipt_path=str(self.store.save_receipt(receipt)), notes=text)
            if len(photos) > 1:
                reply("I'll take the first photo. Send each receipt in its own message.")
            self._advance(phone, draft, reply)
            return

        draft = self.store.draft(phone)
        if draft is None or draft.asking is None:
            reply("Send me a photo of a receipt, and add the project or a note in the same message if you like.")
            return

        self._answer(phone, draft, text, reply)

    def _advance(self, phone: str, draft: Draft, reply: Reply) -> None:
        """Moves the receipt forward until it needs an answer from the employee, or is created."""
        draft.asking = None

        if draft.employee_id is None:
            employee = self._employee_for(phone)
            if employee is None:
                self._ask(phone, draft, "employee", [], f"I don't recognize this number in {self.erp.name}. Reply with your employee ID or work email.", reply)
                return
            draft.employee_id = employee

        employee = self._employee(draft)

        if draft.reading is None:
            reply("Got it. Reading your receipt...")
            try:
                reading = self.reader.read(Path(draft.receipt_path).read_bytes(), draft.notes, self.erp.expense_items(), self.erp.projects(employee))
            except ReceiptNotRead:
                self.store.save_draft(phone, None)
                reply("I couldn't read that receipt. Please send a sharper photo, flat and well lit.")
                return
            draft.reading = asdict(reading)

        reading = ReceiptReading(**draft.reading)

        if reading.total is None or (reading.currency and reading.currency.upper() != self.base_currency):
            seen = f" It shows {reading.total:.2f} {reading.currency}." if reading.total is not None and reading.currency else ""
            self._ask(phone, draft, "amount", [], f"What amount should I claim, in {self.base_currency}?{seen}", reply)
            return

        if reading.expense_item is None:
            items = self.erp.expense_items()[:MAX_OPTIONS]
            self._ask(phone, draft, "expense_item", [item.id for item in items], "What kind of expense is this?\n" + _numbered(items), reply)
            return

        if reading.project_hint and reading.project is None:
            candidates = _closest(reading.project_hint, self.erp.projects(employee))
            question = f'Which project is "{reading.project_hint}"?\n' + _numbered(candidates) + "\n0. No project"
            self._ask(phone, draft, "project", [project.id for project in candidates], question, reply)
            return

        self._create(phone, draft, reading, reply)

    def _answer(self, phone: str, draft: Draft, text: str, reply: Reply) -> None:
        reading = ReceiptReading(**draft.reading) if draft.reading else None

        if draft.asking == "employee":
            employee = self.erp.employee_by_answer(text)
            if employee is None:
                reply(f'I couldn\'t find "{text}" in {self.erp.name}. Reply with your employee ID or work email.')
                return
            self.store.remember_employee(phone, employee.id)
            draft.employee_id = employee.id

        elif draft.asking == "amount":
            amount = _amount(text)
            if amount is None:
                reply(f"Please reply with the amount only, such as 42.50 ({self.base_currency}).")
                return
            original = f" ({reading.total:.2f} {reading.currency})" if reading.total is not None and reading.currency else ""
            reading.total, reading.currency = amount, self.base_currency
            reading.description = (reading.description + original)[:60]
            draft.reading = asdict(reading)

        elif draft.asking == "expense_item":
            choice = _pick(text, draft.options, self.erp.expense_items())
            if choice is None:
                reply("Please reply with one of the numbers.")
                return
            reading.expense_item = choice
            draft.reading = asdict(reading)

        elif draft.asking == "project":
            if text.strip().lower() in {"0", "none", "no"}:
                reading.project_hint = None
            else:
                choice = _pick(text, draft.options, self.erp.projects(self._employee(draft)))
                if choice is None:
                    reply("Please reply with one of the numbers, or 0 for no project.")
                    return
                reading.project = choice
            draft.reading = asdict(reading)

        self._advance(phone, draft, reply)

    def _create(self, phone: str, draft: Draft, reading: ReceiptReading, reply: Reply) -> None:
        employee = self._employee(draft)
        created: CreatedExpense = self.erp.create_expense(
            Expense(
                employee=employee,
                date=reading.date or self.today().isoformat(),
                amount=f"{reading.total:.2f}",
                expense_item=reading.expense_item,
                description=reading.description,
                project=reading.project,
                receipt=Path(draft.receipt_path),
                project_task=next((p.task for p in self.erp.projects(employee) if p.id == reading.project), None),
            )
        )
        self.store.save_draft(phone, None)

        project = f", project {reading.project}" if reading.project else ""
        link = f"\n{created.url}" if created.url else ""
        reply(f"Created {self.erp.expense_noun} {created.id}: {reading.total:.2f} {self.base_currency}, {reading.expense_item}{project}.{link}")

    def _employee(self, draft: Draft):
        return next(e for e in self.erp.employees() if e.id == draft.employee_id)

    def _employee_for(self, phone: str) -> str | None:
        known = self.store.known_employee(phone)
        if known and any(e.id == known for e in self.erp.employees()):
            return known
        employee = self.erp.employee_by_phone(phone)
        return employee.id if employee else None

    def _ask(self, phone: str, draft: Draft, asking: str, options: list[str], question: str, reply: Reply) -> None:
        draft.asking = asking
        draft.options = options
        self.store.save_draft(phone, draft)
        reply(question)


def _numbered(choices: list[Choice]) -> str:
    return "\n".join(f"{index}. {choice.description or choice.id}" for index, choice in enumerate(choices, start=1))


def _pick(text: str, options: list[str], choices: list[Choice]) -> str | None:
    """Reads an answer to a numbered question: the number, or the ID or description of one of the choices."""
    answer = text.strip()
    if answer.isdigit() and 1 <= int(answer) <= len(options):
        return options[int(answer) - 1]
    lowered = answer.lower()
    for choice in choices:
        if lowered in {choice.id.lower(), choice.description.lower()}:
            return choice.id
    return None


def _closest(hint: str, projects: list[Choice]) -> list[Choice]:
    """Returns the projects whose ID or description looks most like the employee's words."""
    def score(project: Choice) -> float:
        words = hint.lower()
        return max(
            difflib.SequenceMatcher(None, words, project.id.lower()).ratio(),
            difflib.SequenceMatcher(None, words, project.description.lower()).ratio(),
        )

    return sorted(projects, key=score, reverse=True)[: MAX_OPTIONS - 1]


def _amount(text: str) -> float | None:
    """Reads an amount such as 42.50, 42,50 or 1,234.50; commas before three digits separate thousands."""
    match = re.search(r"\d+(?:[.,]\d{1,2})?(?!\d)", re.sub(r",(?=\d{3}(?!\d))", "", text))
    amount = float(match.group().replace(",", ".")) if match else None
    return amount if amount else None


def _velixo_failure(error: VelixoError) -> str:
    if error.code in {"NOT_SIGNED_IN", "SESSION_ENDED", "ACCESS_DENIED", "CLI_NOT_FOUND"}:
        return "The expense system is offline right now. Please try again later."
    return "I couldn't save the expense in the ERP. A manager can check the receipt in the system before you send it again."
