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
from .scan import black_and_white, straighten_receipt
from .velixo import VelixoError

Reply = Callable[[str], None]
Buttons = Callable[[str, list[tuple[str, str]]], None]

MAX_OPTIONS = 9
PAGE = 8


@dataclass
class Out:
    """Where the conversation's replies go. `buttons`, when the channel has them, shows a question with its choices
    as buttons, each a label and the answer a tap sends back; without it, a question goes out as text."""

    send: Reply
    buttons: Buttons | None = None

    def __call__(self, text: str) -> None:
        self.send(text)


@dataclass
class Draft:
    """A receipt on its way to the ERP, kept between messages while a question waits for its answer."""

    receipt_path: str
    notes: str
    scan_path: str | None = None
    employee_id: str | None = None
    reading: dict | None = None
    asking: str | None = None
    options: list[str] = field(default_factory=list)
    duplicate_checked: bool = False
    page: int = 0
    project_task: str | None = None
    confirmed: bool = False


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

    def learned_item(self, merchant: str | None) -> str | None:
        """Returns the expense item an employee last chose for this merchant."""
        with self._lock:
            return self._load().get("items", {}).get(_merchant_key(merchant)) if merchant else None

    def learn_item(self, merchant: str | None, item: str) -> None:
        if not merchant:
            return
        with self._lock:
            data = self._load()
            data.setdefault("items", {})[_merchant_key(merchant)] = item
            self._save(data)

    def save_receipt(self, receipt: bytes, scan: bytes) -> tuple[Path, Path]:
        """Saves the straightened photo, which the reader reads, and its black and white scan, which the ERP keeps."""
        folder = self.folder / "receipts"
        folder.mkdir(exist_ok=True)
        name = f"receipt-{uuid.uuid4().hex[:12]}"
        photo, scanned = folder / f"{name}.jpg", folder / f"{name}.png"
        photo.write_bytes(receipt)
        scanned.write_bytes(scan)
        return photo, scanned

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

    def handle(self, phone: str, text: str, photos: list[bytes], send: Reply, buttons: Buttons | None = None) -> None:
        """Handles one incoming message. Messages from one phone are handled one at a time, in order. With
        `buttons`, questions show their choices as buttons, and the expense waits for a tap on Create."""
        reply = Out(send, buttons)
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

    def _handle(self, phone: str, text: str, photos: list[bytes], reply: Out) -> None:
        if photos:
            try:
                receipt = straighten_receipt(photos[0])
                scan = black_and_white(receipt)
            except ValueError:
                reply("I couldn't open that photo. Please send the receipt as a JPEG or PNG picture.")
                return
            photo, scanned = self.store.save_receipt(receipt, scan)
            draft = Draft(receipt_path=str(photo), scan_path=str(scanned), notes=text)
            if len(photos) > 1:
                reply("I'll take the first photo. Send each receipt in its own message.")
            self._advance(phone, draft, reply)
            return

        draft = self.store.draft(phone)
        if draft is None or draft.asking is None:
            reply("Send me a photo of a receipt, and add the project or a note in the same message if you like.")
            return

        self._answer(phone, draft, text, reply)

    def _advance(self, phone: str, draft: Draft, reply: Out) -> None:
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

        items = self.erp.expense_items()
        if reading.expense_item is None and self.store.learned_item(reading.merchant) in {item.id for item in items}:
            reading.expense_item = self.store.learned_item(reading.merchant)
            draft.reading = asdict(reading)

        if reading.expense_item is None:
            top = [item for wanted in reading.expense_item_candidates for item in items if item.id == wanted][:3]
            if top:
                self._choose(phone, draft, "expense_item", top, "What kind of expense is this?", reply,
                             footer="\nOr reply with another item's ID.", extra=[("Other…", "other")])
            elif reply.buttons:
                self._page(phone, draft, "pick_item", items, "What kind of expense is this?", reply)
            else:
                self._choose(phone, draft, "expense_item", items[:MAX_OPTIONS], "What kind of expense is this?", reply)
            return

        if reading.project_hint and reading.project is None:
            candidates = _ranked(reading.project_hint, self.erp.projects(employee))[: MAX_OPTIONS - 1]
            self._choose(phone, draft, "project", candidates, f'Which project is "{reading.project_hint}"?', reply,
                         footer="\n0. No project", extra=[("No project", "none")])
            return

        if reply.buttons and not draft.confirmed:
            choices = [("✅ Create", "create"), ("📁 Project…", "project"), ("🏷 Expense item…", "item")]
            draft.asking, draft.options = "summary", [value for _, value in choices]
            self.store.save_draft(phone, draft)
            reply.buttons(self._summary(draft, reading), choices)
            return

        if not draft.duplicate_checked:
            draft.duplicate_checked = True
            existing = self.erp.similar_expense(employee, self._date(reading), f"{reading.total:.2f}")
            if existing:
                about = f" ({existing.description})" if existing.description else ""
                question = (
                    f"You already have {self.erp.expense_noun} {existing.id} for {existing.amount} "
                    f"{existing.currency or self.base_currency} on {existing.date}{about}. Create another one?"
                )
                if reply.buttons:
                    draft.asking, draft.options = "duplicate", ["yes", "no"]
                    self.store.save_draft(phone, draft)
                    reply.buttons(question, [("✅ Create anyway", "yes"), ("✖ Cancel", "no")])
                else:
                    self._ask(phone, draft, "duplicate", [], question + " Reply YES to create it, or NO to stop.", reply)
                return

        self._create(phone, draft, reading, reply)

    def _answer(self, phone: str, draft: Draft, text: str, reply: Out) -> None:
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

        elif draft.asking in {"expense_item", "pick_item"}:
            items = self.erp.expense_items()
            if text.lower() in {"other", "more"}:
                draft.page = draft.page + 1 if text.lower() == "more" and draft.asking == "pick_item" else 0
                self._page(phone, draft, "pick_item", items, "Which expense item?", reply)
                return
            choice = _pick(text, draft.options, items)
            if choice is None:
                reply("Please reply with one of the numbers.")
                return
            reading.expense_item = choice
            draft.reading = asdict(reading)
            self.store.learn_item(reading.merchant, choice)

        elif draft.asking == "summary":
            if text.lower() == "create":
                draft.confirmed = True
            elif text.lower() == "project":
                draft.page = 0
                self._pick_project(phone, draft, reply)
                return
            elif text.lower() == "item":
                draft.page = 0
                self._page(phone, draft, "pick_item", self.erp.expense_items(), "Which expense item?", reply)
                return

        elif draft.asking == "pick_project":
            if text.lower() == "more":
                draft.page += 1
                self._pick_project(phone, draft, reply)
                return
            draft.project_task = None
            if text.lower() in {"none", "no", "0"}:
                reading.project = reading.project_hint = None
            else:
                choice = _pick(text, draft.options, self.erp.projects(self._employee(draft)))
                if choice is None:
                    reply("Please tap one of the projects.")
                    return
                reading.project = choice
                tasks = self.erp.project_tasks(self._employee(draft), choice)
                if len(tasks) > 1:
                    draft.reading, draft.page = asdict(reading), 0
                    self._page(phone, draft, "task", tasks, f"Which task of {choice}?", reply)
                    return
                draft.project_task = tasks[0].id if tasks else None
            draft.reading = asdict(reading)

        elif draft.asking == "task":
            tasks = self.erp.project_tasks(self._employee(draft), reading.project)
            if text.lower() == "more":
                draft.page += 1
                self._page(phone, draft, "task", tasks, f"Which task of {reading.project}?", reply)
                return
            choice = _pick(text, draft.options, tasks)
            if choice is None:
                reply("Please tap one of the tasks.")
                return
            draft.project_task = choice

        elif draft.asking == "duplicate":
            answer = text.strip().lower()
            if answer in {"no", "n", "non"}:
                self.store.save_draft(phone, None)
                reply("OK, I didn't create it.")
                return
            if answer not in {"yes", "y", "oui"}:
                reply("Please reply YES to create the expense, or NO to stop.")
                return

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

    def _create(self, phone: str, draft: Draft, reading: ReceiptReading, reply: Out) -> None:
        employee = self._employee(draft)
        created: CreatedExpense = self.erp.create_expense(
            Expense(
                employee=employee,
                date=self._date(reading),
                amount=f"{reading.total:.2f}",
                expense_item=reading.expense_item,
                description=reading.description,
                project=reading.project,
                receipt=Path(draft.scan_path or draft.receipt_path),
                project_task=draft.project_task or next((p.task for p in self.erp.projects(employee) if p.id == reading.project), None),
            )
        )
        self.store.save_draft(phone, None)

        project = f", project {reading.project}" if reading.project else ""
        link = f"\n{created.url}" if created.url else ""
        reply(f"Created {self.erp.expense_noun} {created.id}: {reading.total:.2f} {self.base_currency}, {reading.expense_item}{project}.{link}")

    def _summary(self, draft: Draft, reading: ReceiptReading) -> str:
        item = next((i.description or i.id for i in self.erp.expense_items() if i.id == reading.expense_item), reading.expense_item)
        summary = f"{reading.description} — {reading.total:.2f} {self.base_currency}, {item}, {self._date(reading)}"
        if reading.project:
            summary += f"\nProject {reading.project}" + (f", task {draft.project_task}" if draft.project_task else "")
        return summary

    def _pick_project(self, phone: str, draft: Draft, reply: Out) -> None:
        projects = _ranked(draft.notes, self.erp.projects(self._employee(draft))) if draft.notes else self.erp.projects(self._employee(draft))
        self._page(phone, draft, "pick_project", projects, "Which project?", reply, extra=[("No project", "none")])

    def _date(self, reading: ReceiptReading) -> str:
        return reading.date or self.today().isoformat()

    def _employee(self, draft: Draft):
        return next(e for e in self.erp.employees() if e.id == draft.employee_id)

    def _employee_for(self, phone: str) -> str | None:
        known = self.store.known_employee(phone)
        if known and any(e.id == known for e in self.erp.employees()):
            return known
        employee = self.erp.employee_by_phone(phone)
        return employee.id if employee else None

    def _ask(self, phone: str, draft: Draft, asking: str, options: list[str], question: str, reply: Out) -> None:
        draft.asking = asking
        draft.options = options
        self.store.save_draft(phone, draft)
        reply(question)

    def _choose(self, phone: str, draft: Draft, asking: str, choices: list[Choice], question: str, reply: Out,
                footer: str = "", extra: list[tuple[str, str]] = ()) -> None:
        """Asks a question with choices: as buttons when the channel has them, else numbered, with `footer` after."""
        if reply.buttons:
            draft.asking, draft.options = asking, [choice.id for choice in choices]
            self.store.save_draft(phone, draft)
            reply.buttons(question, [(_label(choice), choice.id) for choice in choices] + list(extra))
        else:
            self._ask(phone, draft, asking, [choice.id for choice in choices], f"{question}\n{_numbered(choices)}{footer}", reply)

    def _page(self, phone: str, draft: Draft, asking: str, choices: list[Choice], question: str, reply: Out,
              extra: list[tuple[str, str]] = ()) -> None:
        """Asks with one page of `choices`, `draft.page`, and a More… choice while pages remain."""
        start = draft.page * PAGE
        more = [("More…", "more")] if len(choices) > start + PAGE else []
        self._choose(phone, draft, asking, choices[start : start + PAGE], question, reply, extra=more + list(extra))


def _label(choice: Choice) -> str:
    return choice.description or choice.id


def _numbered(choices: list[Choice]) -> str:
    return "\n".join(f"{index}. {_label(choice)}" for index, choice in enumerate(choices, start=1))


def _merchant_key(merchant: str) -> str:
    return " ".join(merchant.lower().split())


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


def _ranked(hint: str, projects: list[Choice]) -> list[Choice]:
    """Returns the projects, the ones whose ID or description looks most like the employee's words first."""
    def score(project: Choice) -> float:
        words = hint.lower()
        return max(
            difflib.SequenceMatcher(None, words, project.id.lower()).ratio(),
            difflib.SequenceMatcher(None, words, project.description.lower()).ratio(),
        )

    return sorted(projects, key=score, reverse=True)


def _amount(text: str) -> float | None:
    """Reads an amount such as 42.50, 42,50 or 1,234.50; commas before three digits separate thousands."""
    match = re.search(r"\d+(?:[.,]\d{1,2})?(?!\d)", re.sub(r",(?=\d{3}(?!\d))", "", text))
    amount = float(match.group().replace(",", ".")) if match else None
    return amount if amount else None


def _velixo_failure(error: VelixoError) -> str:
    if error.code in {"NOT_SIGNED_IN", "SESSION_ENDED", "ACCESS_DENIED", "CLI_NOT_FOUND"}:
        return "The expense system is offline right now. Please try again later."
    return "I couldn't save the expense in the ERP. A manager can check the receipt in the system before you send it again."
