"""The two ERPs the sample supports, behind one interface: who an employee is, what they can expense, and
how an expense is created with its receipt attached."""

from __future__ import annotations

import json
import time
from dataclasses import dataclass
from datetime import date, timedelta
from pathlib import Path
from urllib.parse import quote, urlencode

from .phones import normalize_phone
from .velixo import Velixo

HERE = Path(__file__).resolve().parent.parent / "velixo"


@dataclass
class Employee:
    id: str
    name: str
    email: str | None
    phones: list[str]
    location: str | None = None


@dataclass
class Choice:
    """An expense item, an expense type or a project: the ERP's ID and what people call it. An Acumatica
    project also carries the task its expenses go to."""

    id: str
    description: str
    task: str | None = None


@dataclass
class Expense:
    employee: Employee
    date: str
    amount: str
    expense_item: str
    description: str
    project: str | None
    receipt: Path
    project_task: str | None = None


@dataclass
class ExistingExpense:
    """An expense the ERP already holds, as a likely duplicate of a new receipt."""

    id: str
    date: str
    amount: str
    currency: str | None
    description: str


SIMILAR_DAYS = 3


@dataclass
class CreatedExpense:
    id: str
    url: str | None


class Erp:
    """The ERP behind the sample. Lists are read once and kept for `cache_seconds`."""

    name = "ERP"
    expense_noun = "expense"
    folder = ""

    def __init__(self, velixo: Velixo, cache_seconds: int = 900):
        self.velixo = velixo
        self.cache_seconds = cache_seconds
        self._cache: dict[str, tuple[float, list]] = {}

    def employees(self) -> list[Employee]:
        return self._cached("employees", self._read_employees)

    def expense_items(self) -> list[Choice]:
        return self._cached("expense_items", self._read_expense_items)

    def projects(self, employee: Employee) -> list[Choice]:
        """Returns the projects the employee can charge an expense to."""
        return self._cached(f"projects:{employee.id}", lambda: self._read_projects(employee))

    def project_tasks(self, employee: Employee, project_id: str) -> list[Choice]:
        """Returns the tasks an expense on the project can go to, the default first. Empty when the ERP has none."""
        return []

    def employee_by_phone(self, phone: str) -> Employee | None:
        wanted = normalize_phone(phone)
        matches = [employee for employee in self.employees() if wanted and wanted in employee.phones]
        return matches[0] if len(matches) == 1 else None

    def employee_by_answer(self, answer: str) -> Employee | None:
        """Finds the employee a reply names by ID, email or full name, ignoring case."""
        wanted = answer.strip().lower()
        if not wanted:
            return None
        for employee in self.employees():
            if wanted in {employee.id.strip().lower(), (employee.email or "").lower(), employee.name.lower()}:
                return employee
        return None

    def similar_expense(self, employee: Employee, expense_date: str, amount: str) -> ExistingExpense | None:
        """Returns the employee's expense for the same amount dated within three days of `expense_date`, the
        closest date first, or None. Matching on the amount and a narrow date window is what keeps this a query for
        a handful of rows rather than a read of every expense."""
        try:
            day = date.fromisoformat(expense_date)
        except ValueError:
            return None
        rows = self.velixo.query(
            HERE / self.folder / "similar-expenses.vql",
            {
                "Employee ID": employee.id,
                "Amount": amount,
                "From Date": (day - timedelta(days=SIMILAR_DAYS)).isoformat(),
                "To Date": (day + timedelta(days=SIMILAR_DAYS)).isoformat(),
            },
        )
        found = [
            ExistingExpense(str(row["ID"]).strip(), str(row["Date"])[:10], f"{float(row['Amount']):.2f}", row["Currency"], row["Description"] or "")
            for row in rows
        ]
        return min(found, key=lambda existing: abs((date.fromisoformat(existing.date) - day).days), default=None)

    def create_expense(self, expense: Expense) -> CreatedExpense:
        raise NotImplementedError

    def _cached(self, key: str, read) -> list:
        now = time.monotonic()
        if key in self._cache and now - self._cache[key][0] < self.cache_seconds:
            return self._cache[key][1]
        value = read()
        self._cache[key] = (now, value)
        return value

    def _choices(self, file: Path) -> list[Choice]:
        return [Choice(str(row["ID"]).strip(), row["Description"] or "") for row in self.velixo.query(file)]

    def _read_employees(self) -> list[Employee]:
        raise NotImplementedError

    def _read_expense_items(self) -> list[Choice]:
        raise NotImplementedError

    def _read_projects(self, employee: Employee) -> list[Choice]:
        raise NotImplementedError


@dataclass
class AcumaticaSettings:
    url: str | None = None
    company: str | None = None


class Acumatica(Erp):
    """Expense receipts on the Expense Receipts screen (EP301020), with the photo attached to the receipt."""

    name = "Acumatica"
    expense_noun = "expense receipt"
    folder = "acumatica"

    def __init__(self, velixo: Velixo, settings: AcumaticaSettings | None = None, **kwargs):
        super().__init__(velixo, **kwargs)
        self.settings = settings or AcumaticaSettings()

    def _read_employees(self) -> list[Employee]:
        return [
            Employee(
                id=row["Employee ID"].strip(),
                name=row["Name"] or "",
                email=row["Email"],
                phones=[p for p in (normalize_phone(row[k]) for k in ("Phone 1", "Phone 2", "Phone 3")) if p],
            )
            for row in self.velixo.query(HERE / "acumatica" / "employees.vql")
        ]

    def _read_expense_items(self) -> list[Choice]:
        return self._choices(HERE / "acumatica" / "expense-items.vql")

    def _read_projects(self, employee: Employee) -> list[Choice]:
        """Returns each project that has not expired, with its default task, or its first active task when it
        has no default: Acumatica refuses a receipt with a project and no task."""
        rows = self._cached("all_projects", lambda: self.velixo.query(HERE / "acumatica" / "projects.vql"))
        today = date.today().isoformat()
        projects: dict[str, Choice] = {}
        for row in rows:
            if row["Expires"] and row["Expires"][:10] < today:
                continue
            project_id, task = row["ID"].strip(), row["Task"].strip()
            if project_id not in projects or row["Default Task"]:
                projects[project_id] = Choice(project_id, row["Description"] or "", task)
        return list(projects.values())

    def project_tasks(self, employee: Employee, project_id: str) -> list[Choice]:
        rows = self._cached("all_projects", lambda: self.velixo.query(HERE / "acumatica" / "projects.vql"))
        tasks = [Choice(row["Task"].strip(), row["Task Description"] or "") for row in rows if row["ID"].strip() == project_id and row["Default Task"]]
        tasks += [Choice(row["Task"].strip(), row["Task Description"] or "") for row in rows if row["ID"].strip() == project_id and not row["Default Task"]]
        return tasks

    def create_expense(self, expense: Expense) -> CreatedExpense:
        document = self.velixo.run_batch(
            HERE / "acumatica" / "expense-receipt.batch.json",
            {
                "EmployeeID": expense.employee.id,
                "ReceiptDate": expense.date,
                "ExpenseItem": expense.expense_item,
                "Description": expense.description,
                "Amount": expense.amount,
                "ProjectID": expense.project,
                "ProjectTaskID": expense.project_task if expense.project else None,
                "CostCodeID": self._default_cost_code() if expense.project else None,
            },
            attachments=[expense.receipt],
        )
        receipt_id = str(document["result"]["feedback"]["savedKey"]["ClaimDetailCD"]).strip()
        return CreatedExpense(receipt_id, self._url(receipt_id))

    def _default_cost_code(self) -> str | None:
        """Returns the tenant's default cost code: with cost codes turned on, a project expense needs one."""
        rows = self._cached("cost_code", lambda: self.velixo.query(HERE / "acumatica" / "default-cost-code.vql"))
        return rows[0]["Cost Code"].strip() if rows else None

    def _url(self, receipt_id: str) -> str | None:
        if not self.settings.url:
            return None
        query = {"ScreenId": "EP301020", "ClaimDetailCD": receipt_id}
        if self.settings.company:
            query = {"CompanyID": self.settings.company, **query}
        return f"{self.settings.url.rstrip('/')}/Main?{urlencode(query)}"


@dataclass
class SageIntacctSettings:
    base_currency: str = "USD"
    url_template: str | None = None


class SageIntacct(Erp):
    """Employee expense reports with one line, in the employee's own location, with the photo attached."""

    name = "Sage Intacct"
    expense_noun = "expense report"
    folder = "sageintacct"

    def __init__(self, velixo: Velixo, settings: SageIntacctSettings | None = None, **kwargs):
        super().__init__(velixo, **kwargs)
        self.settings = settings or SageIntacctSettings()

    def _read_employees(self) -> list[Employee]:
        return [
            Employee(
                id=str(row["Employee ID"]).strip(),
                name=row["Name"] or "",
                email=row["Email"],
                phones=[p for p in (normalize_phone(row[k]) for k in ("Phone 1", "Phone 2", "Cell Phone")) if p],
                location=row["Location ID"],
            )
            for row in self.velixo.query(HERE / "sageintacct" / "employees.vql")
        ]

    def _read_expense_items(self) -> list[Choice]:
        return self._choices(HERE / "sageintacct" / "expense-types.vql")

    def _read_projects(self, employee: Employee) -> list[Choice]:
        """Sage Intacct refuses an expense on a project the employee is not a resource of."""
        rows = self.velixo.query(HERE / "sageintacct" / "projects.vql", {"Employee ID": employee.id})
        return [Choice(str(row["ID"]).strip(), row["Description"] or "") for row in rows]

    def create_expense(self, expense: Expense) -> CreatedExpense:
        document = self.velixo.run_batch(
            HERE / "sageintacct" / "expense-report.batch.json",
            {
                "EmployeeID": expense.employee.id,
                "ExpenseDate": expense.date,
                "Description": expense.description,
                "BaseCurrency": self.settings.base_currency,
            },
            rows=[
                {
                    "LocationID": expense.employee.location,
                    "ExpenseType": expense.expense_item,
                    "Amount": expense.amount,
                    "LineDate": expense.date,
                    "Memo": expense.description,
                    "ProjectID": expense.project,
                }
            ],
            attachments=[expense.receipt],
        )
        report_id = _sage_key(document["result"])
        url = self.settings.url_template.format(id=quote(report_id)) if self.settings.url_template else None
        return CreatedExpense(report_id, url)


def _sage_key(result: dict) -> str:
    """Returns the key of the record a Sage Intacct batch saved. The runner reports it as JSON text in
    `textualResponse`, not in the `feedback.savedKey` an Acumatica screen batch returns."""
    if isinstance(result.get("feedback"), dict) and result["feedback"].get("savedKey"):
        saved_key = result["feedback"]["savedKey"]
        return str(saved_key.get("key") or saved_key.get("id") or next(iter(saved_key.values()))).strip()
    text = result.get("textualResponse")
    outcome = json.loads(text) if isinstance(text, str) else (result.get("data") or {})
    return str(outcome["key"]).strip()
