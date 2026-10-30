from datetime import date

import pytest

from expenses.conversation import Conversation, Store
from expenses.erp import Choice, CreatedExpense, Employee, Erp
from expenses.reader import ReceiptNotRead, ReceiptReading
from expenses.velixo import VelixoError
from tests.receipt_photo import receipt_photo

KNOWN_PHONE = "+17773452246"
UNKNOWN_PHONE = "+15550001111"


class FakeErp(Erp):
    name = "Acumatica"
    expense_noun = "expense receipt"

    def __init__(self):
        super().__init__(velixo=None)
        self.created = []
        self.failure: VelixoError | None = None

    def _read_employees(self):
        return [
            Employee("EP00000002", "Maxwell Baker", "mbaker@revisiontwo.com", ["7773452246"]),
            Employee("EP00000003", "Layla Beauvoir", "lbeauvoir@revisiontwo.com", ["7777652577"]),
        ]

    def _read_expense_items(self):
        return [Choice("MEALS", "Business dinner and lunch"), Choice("TRAVEL", "Business Travel - Air, Train, Miles")]

    def _read_projects(self, employee):
        return [Choice("INTERNAL25", "Internal projects 2025 - track expenses"), Choice("TMR03", "Marlin Beverage Process Re-architecture")]

    def create_expense(self, expense):
        if self.failure:
            raise self.failure
        self.created.append(expense)
        return CreatedExpense("000123", "https://erp.example/Main?ScreenId=EP301020&ClaimDetailCD=000123")


class FakeReader:
    def __init__(self, **overrides):
        self.reading = dict(
            merchant="The Corner Bistro", date="2026-10-28", total=54.22, currency="USD", expense_item="MEALS",
            project=None, project_hint=None, description="Lunch at The Corner Bistro",
        ) | overrides
        self.calls = []
        self.fails = False

    def read(self, receipt_jpeg, notes, expense_items, projects):
        self.calls.append(notes)
        if self.fails:
            raise ReceiptNotRead("unreadable")
        return ReceiptReading(**self.reading)


@pytest.fixture
def erp():
    return FakeErp()


def talk(erp, reader, tmp_path):
    conversation = Conversation(erp, reader, Store(tmp_path), today=lambda: date(2026, 10, 30))
    replies = []

    def send(phone, text="", photo=False):
        conversation.handle(phone, text, [receipt_photo()] if photo else [], replies.append)
        return replies[-1]

    return send, replies


def test_creates_the_expense_from_a_photo_and_replies_with_its_link(erp, tmp_path):
    send, _ = talk(erp, FakeReader(), tmp_path)

    answer = send(KNOWN_PHONE, "lunch with the Marlin team", photo=True)

    assert answer.startswith("Created expense receipt 000123: 54.22 USD, MEALS.")
    assert answer.endswith("https://erp.example/Main?ScreenId=EP301020&ClaimDetailCD=000123")
    expense = erp.created[0]
    assert (expense.employee.id, expense.date, expense.amount, expense.expense_item) == ("EP00000002", "2026-10-28", "54.22", "MEALS")
    assert expense.receipt.exists(), "the straightened receipt is what gets attached"


def test_passes_the_employees_words_to_the_reader(erp, tmp_path):
    reader = FakeReader()
    send, _ = talk(erp, reader, tmp_path)

    send(KNOWN_PHONE, "project TMR03, dinner with client", photo=True)

    assert reader.calls == ["project TMR03, dinner with client"]


def test_asks_who_an_unknown_number_is_then_remembers_it(erp, tmp_path):
    send, _ = talk(erp, FakeReader(), tmp_path)

    assert "don't recognize this number" in send(UNKNOWN_PHONE, photo=True)
    assert send(UNKNOWN_PHONE, "LBEAUVOIR@revisiontwo.com").startswith("Created expense receipt")
    assert erp.created[0].employee.id == "EP00000003"

    send(UNKNOWN_PHONE, photo=True)
    assert erp.created[1].employee.id == "EP00000003", "the second receipt needs no question"


def test_asks_again_when_the_employee_answer_matches_nobody(erp, tmp_path):
    send, _ = talk(erp, FakeReader(), tmp_path)

    send(UNKNOWN_PHONE, photo=True)
    assert "couldn't find" in send(UNKNOWN_PHONE, "nobody@example.com")
    assert send(UNKNOWN_PHONE, "EP00000003").startswith("Created")


def test_asks_for_the_expense_item_only_when_none_fits(erp, tmp_path):
    send, _ = talk(erp, FakeReader(expense_item=None), tmp_path)

    question = send(KNOWN_PHONE, photo=True)
    assert "1. Business dinner and lunch" in question and "2. Business Travel" in question

    assert send(KNOWN_PHONE, "2").startswith("Created expense receipt 000123: 54.22 USD, TRAVEL.")


def test_asks_for_the_project_when_the_employee_named_one_that_does_not_match(erp, tmp_path):
    send, _ = talk(erp, FakeReader(project_hint="marlin beverage"), tmp_path)

    question = send(KNOWN_PHONE, "marlin beverage", photo=True)
    assert question.startswith('Which project is "marlin beverage"?')
    assert question.splitlines()[1] == "1. Marlin Beverage Process Re-architecture"

    assert "project TMR03" in send(KNOWN_PHONE, "1")
    assert erp.created[0].project == "TMR03"


def test_creates_without_a_project_when_the_employee_answers_zero(erp, tmp_path):
    send, _ = talk(erp, FakeReader(project_hint="the big one"), tmp_path)

    send(KNOWN_PHONE, photo=True)
    send(KNOWN_PHONE, "0")

    assert erp.created[0].project is None


def test_asks_for_the_amount_in_the_base_currency_for_a_foreign_receipt(erp, tmp_path):
    send, _ = talk(erp, FakeReader(total=48.0, currency="EUR"), tmp_path)

    assert send(KNOWN_PHONE, photo=True) == "What amount should I claim, in USD? It shows 48.00 EUR."
    send(KNOWN_PHONE, "52.10")

    assert erp.created[0].amount == "52.10"
    assert erp.created[0].description.endswith("(48.00 EUR)")


def test_uses_today_when_the_receipt_has_no_readable_date(erp, tmp_path):
    send, _ = talk(erp, FakeReader(date=None), tmp_path)

    send(KNOWN_PHONE, photo=True)

    assert erp.created[0].date == "2026-10-30"


def test_asks_for_a_new_photo_when_the_receipt_cannot_be_read(erp, tmp_path):
    reader = FakeReader()
    reader.fails = True
    send, _ = talk(erp, reader, tmp_path)

    assert "couldn't read that receipt" in send(KNOWN_PHONE, photo=True)
    assert erp.created == []


def test_explains_what_to_send_when_a_text_arrives_with_no_receipt_waiting(erp, tmp_path):
    send, _ = talk(erp, FakeReader(), tmp_path)

    assert send(KNOWN_PHONE, "hello").startswith("Send me a photo of a receipt")


def test_does_not_invite_a_resend_when_the_erp_refuses_the_expense(erp, tmp_path):
    erp.failure = VelixoError("TASK_FAILED", "The expense item is inactive.", 6)
    send, _ = talk(erp, FakeReader(), tmp_path)

    assert "A manager can check the receipt" in send(KNOWN_PHONE, photo=True)


def test_says_the_system_is_offline_when_the_cli_is_signed_out(erp, tmp_path):
    erp.failure = VelixoError("NOT_SIGNED_IN", "Run velixo login.", 4)
    send, _ = talk(erp, FakeReader(), tmp_path)

    assert "offline" in send(KNOWN_PHONE, photo=True)


@pytest.mark.parametrize("text, amount", [("52.10", 52.10), ("52,10", 52.10), ("1,234", 1234.0), ("1,234.50", 1234.50), ("$ 40", 40.0)])
def test_reads_the_amount_the_employee_types(erp, tmp_path, text, amount):
    send, _ = talk(erp, FakeReader(total=None), tmp_path)

    send(KNOWN_PHONE, photo=True)
    send(KNOWN_PHONE, text)

    assert erp.created[0].amount == f"{amount:.2f}"


def test_takes_no_for_no_project_whatever_its_case(erp, tmp_path):
    send, _ = talk(erp, FakeReader(project_hint="the big one"), tmp_path)

    send(KNOWN_PHONE, photo=True)
    send(KNOWN_PHONE, "No")

    assert erp.created[0].project is None


def test_forgets_the_receipt_when_the_erp_refuses_it_so_a_reply_cannot_create_it_twice(erp, tmp_path):
    send, _ = talk(erp, FakeReader(project_hint="marlin"), tmp_path)
    send(KNOWN_PHONE, photo=True)
    erp.failure = VelixoError("TIMEOUT", "No answer.", 1)
    send(KNOWN_PHONE, "1")
    erp.failure = None

    assert send(KNOWN_PHONE, "1").startswith("Send me a photo")
    assert erp.created == []


def test_answers_even_when_something_unexpected_fails(erp, tmp_path):
    reader = FakeReader()
    reader.read = lambda *args: (_ for _ in ()).throw(ConnectionError("network down"))
    send, _ = talk(erp, reader, tmp_path)

    assert "Something went wrong" in send(KNOWN_PHONE, photo=True)
