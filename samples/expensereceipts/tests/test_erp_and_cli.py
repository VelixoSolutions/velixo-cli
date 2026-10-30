import json
import os
import stat
import sys
from pathlib import Path

import pytest

from expenses.erp import Acumatica, AcumaticaSettings, Employee, Expense, SageIntacct, SageIntacctSettings
from expenses.velixo import Velixo, VelixoError

FAKE_CLI = """#!{python}
import json, os, sys
args = sys.argv[1:]
log = os.environ["FAKE_VELIXO_LOG"]
rows = None
if "--rows" in args:
    rows = json.load(open(args[args.index("--rows") + 1]))
with open(log, "a") as f:
    f.write(json.dumps({{"args": args, "rows": rows}}) + "\\n")
answer = os.environ.get("FAKE_VELIXO_QUERY_ANSWER") if args[0] == "query" and os.environ.get("FAKE_VELIXO_QUERY_ANSWER") else os.environ.get("FAKE_VELIXO_ANSWER", "{{}}")
print(answer)
sys.exit(int(os.environ.get("FAKE_VELIXO_EXIT", "0")))
"""


@pytest.fixture
def cli(tmp_path, monkeypatch):
    binary = tmp_path / "velixo"
    binary.write_text(FAKE_CLI.format(python=sys.executable))
    binary.chmod(binary.stat().st_mode | stat.S_IEXEC)
    log = tmp_path / "calls.log"
    monkeypatch.setenv("FAKE_VELIXO_LOG", str(log))

    def answer(document, exit_code=0, queries=None):
        monkeypatch.setenv("FAKE_VELIXO_ANSWER", json.dumps(document))
        monkeypatch.setenv("FAKE_VELIXO_EXIT", str(exit_code))
        if queries is not None:
            monkeypatch.setenv("FAKE_VELIXO_QUERY_ANSWER", json.dumps(queries))

    def calls():
        return [json.loads(line) for line in log.read_text().splitlines()]

    return Velixo(connection="test-erp", binary=str(binary)), answer, calls


def expense(tmp_path, project=None, task=None):
    receipt = tmp_path / "receipt.jpg"
    receipt.write_bytes(b"jpeg")
    return Expense(
        employee=Employee("1103", "Gabriel", "gabriel@velixo.com", [], location="100"),
        date="2026-10-28", amount="54.22", expense_item="Meals", description="Lunch", project=project, receipt=receipt, project_task=task,
    )


def test_queries_return_rows_keyed_by_caption(cli):
    velixo, answer, calls = cli
    answer({"result": {"data": {"columns": [{"caption": "ID"}, {"caption": "Description"}], "rows": [["MEALS", "Meals"]]}}})

    assert velixo.query(Path("items.vql")) == [{"ID": "MEALS", "Description": "Meals"}]
    args = calls()[0]["args"]
    assert args[:4] == ["query", "run", "--query", "items.vql"]
    assert "--all" in args and args[args.index("--connection") + 1] == "test-erp"


def test_acumatica_creates_the_receipt_with_the_photo_and_links_to_it(cli, tmp_path):
    velixo, answer, calls = cli
    answer(
        {"result": {"feedback": {"savedKey": {"ClaimDetailCD": "000123"}}}},
        queries={"result": {"data": {"columns": [{"caption": "Cost Code"}, {"caption": "Description"}], "rows": [["00000", "DEFAULT"]]}}},
    )
    erp = Acumatica(velixo, AcumaticaSettings(url="https://acme.acumatica.com/", company="Company"))

    created = erp.create_expense(expense(tmp_path, project="INTERNAL25", task="01OPS"))

    assert created.id == "000123"
    assert created.url == "https://acme.acumatica.com/Main?CompanyID=Company&ScreenId=EP301020&ClaimDetailCD=000123"
    args = calls()[-1]["args"]
    assert args[:3] == ["command", "run", "--batch"] and args[3].endswith("acumatica/expense-receipt.batch.json")
    for param in ["EmployeeID=1103", "ReceiptDate=2026-10-28", "ExpenseItem=Meals", "Amount=54.22", "ProjectID=INTERNAL25", "ProjectTaskID=01OPS", "CostCodeID=00000"]:
        assert param in args
    assert args[args.index("--attach") + 1].endswith("receipt.jpg")
    assert "--yes" in args


def test_acumatica_leaves_the_project_at_its_default_when_there_is_none(cli, tmp_path):
    velixo, answer, calls = cli
    answer({"result": {"feedback": {"savedKey": {"ClaimDetailCD": "000124"}}}})

    Acumatica(velixo).create_expense(expense(tmp_path))

    args = calls()[0]["args"]
    nulls = [args[i + 1] for i, arg in enumerate(args) if arg == "--param-null"]
    assert nulls == ["ProjectID", "ProjectTaskID", "CostCodeID"]


def test_sage_creates_a_one_line_report_in_the_employees_location(cli, tmp_path):
    velixo, answer, calls = cli
    answer({"result": {"textualResponse": json.dumps({"object": "expenses/employee-expense", "operation": "created", "key": "3558"})}})
    erp = SageIntacct(velixo, SageIntacctSettings(base_currency="USD", url_template="https://sage.example/expense/{id}"))

    created = erp.create_expense(expense(tmp_path, project="10001"))

    assert (created.id, created.url) == ("3558", "https://sage.example/expense/3558")
    call = calls()[0]
    assert "BaseCurrency=USD" in call["args"] and "EmployeeID=1103" in call["args"]
    assert call["rows"] == [{"values": {
        "LocationID": "100", "ExpenseType": "Meals", "Amount": "54.22", "LineDate": "2026-10-28", "Memo": "Lunch", "ProjectID": "10001",
    }}]


def test_reports_the_cli_error_code(cli):
    velixo, answer, _ = cli
    answer({"error": {"code": "NOT_SIGNED_IN", "message": "Run velixo login.", "exitCode": 4}}, exit_code=4)

    with pytest.raises(VelixoError) as raised:
        velixo.query(Path("x.vql"))
    assert (raised.value.code, raised.value.exit_code) == ("NOT_SIGNED_IN", 4)


def test_reports_a_missing_cli():
    with pytest.raises(VelixoError) as raised:
        Velixo(binary="/nonexistent/velixo").query(Path("x.vql"))
    assert raised.value.code == "CLI_NOT_FOUND"
