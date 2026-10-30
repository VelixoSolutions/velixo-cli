from datetime import date

import pytest

from expenses.conversation import Conversation, Store
from expenses.erp import Choice, CreatedExpense, Employee, Erp, ExistingExpense
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
        self.existing: ExistingExpense | None = None
        self.similar_asked = []

    def _read_employees(self):
        return [
            Employee("EP00000002", "Maxwell Baker", "mbaker@revisiontwo.com", ["7773452246"]),
            Employee("EP00000003", "Layla Beauvoir", "lbeauvoir@revisiontwo.com", ["7777652577"]),
        ]

    def _read_expense_items(self):
        return [Choice("MEALS", "Business dinner and lunch"), Choice("TRAVEL", "Business Travel - Air, Train, Miles")]

    def _read_projects(self, employee):
        return [Choice("INTERNAL25", "Internal projects 2025 - track expenses"), Choice("TMR03", "Marlin Beverage Process Re-architecture")]

    def project_tasks(self, employee, project_id):
        return {"TMR03": [Choice("01", "Design"), Choice("02", "Build")], "INTERNAL25": [Choice("01OPS", "Operations")]}.get(project_id, [])

    def similar_expense(self, employee, expense_date, amount):
        self.similar_asked.append((employee.id, expense_date, amount))
        return self.existing

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


def talk_with_buttons(erp, reader, tmp_path):
    """Talks as Telegram does: each question comes with its choices, and a tap sends a choice's answer back."""
    conversation = Conversation(erp, reader, Store(tmp_path), today=lambda: date(2026, 10, 30))
    said = []

    def send(text="", photo=False):
        conversation.handle(KNOWN_PHONE, text, [receipt_photo()] if photo else [], lambda reply: said.append((reply, None)),
                            lambda question, choices: said.append((question, choices)))
        return said[-1]

    return send, said


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


def test_claims_the_total_without_asking_when_the_receipt_prints_no_currency_code(erp, tmp_path):
    send, _ = talk(erp, FakeReader(currency=None), tmp_path)

    assert send(KNOWN_PHONE, photo=True).startswith("Created expense receipt 000123: 54.22 USD")


def test_attaches_the_black_and_white_scan(erp, tmp_path):
    send, _ = talk(erp, FakeReader(), tmp_path)

    send(KNOWN_PHONE, photo=True)

    assert erp.created[0].receipt.suffix == ".png"


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


def test_checks_for_a_similar_expense_with_the_employee_amount_and_date(erp, tmp_path):
    send, _ = talk(erp, FakeReader(), tmp_path)

    send(KNOWN_PHONE, photo=True)

    assert erp.similar_asked == [("EP00000002", "2026-10-28", "54.22")]


def test_asks_before_creating_an_expense_that_looks_like_one_already_there(erp, tmp_path):
    erp.existing = ExistingExpense("000834", "2026-10-28", "54.22", "USD", "Lunch at The Corner Bistro")
    send, _ = talk(erp, FakeReader(), tmp_path)

    assert send(KNOWN_PHONE, photo=True) == (
        "You already have expense receipt 000834 for 54.22 USD on 2026-10-28 (Lunch at The Corner Bistro). "
        "Create another one? Reply YES to create it, or NO to stop."
    )
    assert erp.created == []


@pytest.mark.parametrize("answer", ["YES", "y", "Oui"])
def test_creates_the_expense_anyway_when_the_employee_says_yes(erp, tmp_path, answer):
    erp.existing = ExistingExpense("000834", "2026-10-28", "54.22", "USD", "")
    send, _ = talk(erp, FakeReader(), tmp_path)

    send(KNOWN_PHONE, photo=True)

    assert send(KNOWN_PHONE, answer).startswith("Created expense receipt 000123")
    assert len(erp.similar_asked) == 1, "asked once, not again after the yes"


@pytest.mark.parametrize("answer", ["no", "N", "non"])
def test_drops_the_receipt_when_the_employee_says_no(erp, tmp_path, answer):
    erp.existing = ExistingExpense("000834", "2026-10-28", "54.22", "USD", "")
    send, _ = talk(erp, FakeReader(), tmp_path)

    send(KNOWN_PHONE, photo=True)

    assert send(KNOWN_PHONE, answer) == "OK, I didn't create it."
    assert erp.created == []
    assert send(KNOWN_PHONE, "yes").startswith("Send me a photo"), "the receipt is gone"


def test_asks_again_when_the_answer_is_neither_yes_nor_no(erp, tmp_path):
    erp.existing = ExistingExpense("000834", "2026-10-28", "54.22", "USD", "")
    send, _ = talk(erp, FakeReader(), tmp_path)

    send(KNOWN_PHONE, photo=True)

    assert send(KNOWN_PHONE, "maybe") == "Please reply YES to create the expense, or NO to stop."
    assert erp.created == []


def test_asks_with_the_readers_three_likeliest_items_when_it_is_unsure(erp, tmp_path):
    send, _ = talk(erp, FakeReader(expense_item=None, expense_item_candidates=["TRAVEL", "MEALS"]), tmp_path)

    question = send(KNOWN_PHONE, photo=True)
    assert question == "What kind of expense is this?\n1. Business Travel - Air, Train, Miles\n2. Business dinner and lunch\nOr reply with another item's ID."

    assert send(KNOWN_PHONE, "meals").startswith("Created expense receipt 000123: 54.22 USD, MEALS.")


def test_remembers_the_item_chosen_for_a_merchant_and_does_not_ask_again(erp, tmp_path):
    send, _ = talk(erp, FakeReader(expense_item=None, expense_item_candidates=["MEALS", "TRAVEL"]), tmp_path)
    send(KNOWN_PHONE, photo=True)
    send(KNOWN_PHONE, "2")

    assert send(KNOWN_PHONE, photo=True).startswith("Created expense receipt 000123: 54.22 USD, TRAVEL.")
    assert [expense.expense_item for expense in erp.created] == ["TRAVEL", "TRAVEL"]


def test_with_buttons_shows_a_summary_and_creates_on_create(erp, tmp_path):
    send, _ = talk_with_buttons(erp, FakeReader(), tmp_path)

    question, choices = send(photo=True)
    assert question == "Lunch at The Corner Bistro — 54.22 USD, Business dinner and lunch, 2026-10-28"
    assert choices == [("✅ Create", "create"), ("📁 Project…", "project"), ("🏷 Expense item…", "item")]
    assert erp.created == []

    assert send("create")[0].startswith("Created expense receipt 000123")


def test_with_buttons_picks_a_project_then_its_task_before_creating(erp, tmp_path):
    send, _ = talk_with_buttons(erp, FakeReader(), tmp_path)
    send("marlin lunch", photo=True)

    question, choices = send("project")
    assert question == "Which project?"
    assert choices[0] == ("Marlin Beverage Process Re-architecture", "TMR03"), "the project closest to the note comes first"
    assert choices[-1] == ("No project", "none")

    question, choices = send("TMR03")
    assert (question, choices) == ("Which task of TMR03?", [("Design", "01"), ("Build", "02")])

    question, _ = send("02")
    assert question.endswith("\nProject TMR03, task 02")

    send("create")
    assert (erp.created[0].project, erp.created[0].project_task) == ("TMR03", "02")


def test_with_buttons_takes_a_projects_only_task_without_asking(erp, tmp_path):
    send, _ = talk_with_buttons(erp, FakeReader(), tmp_path)
    send(photo=True)
    send("project")

    question, _ = send("INTERNAL25")

    assert question.endswith("\nProject INTERNAL25, task 01OPS")


def test_with_buttons_offers_the_likeliest_items_then_every_item_on_other(erp, tmp_path):
    send, _ = talk_with_buttons(erp, FakeReader(expense_item=None, expense_item_candidates=["TRAVEL"]), tmp_path)

    question, choices = send(photo=True)
    assert (question, choices) == ("What kind of expense is this?", [("Business Travel - Air, Train, Miles", "TRAVEL"), ("Other…", "other")])

    question, choices = send("other")
    assert question == "Which expense item?"
    assert [value for _, value in choices] == ["MEALS", "TRAVEL"]

    assert send("MEALS")[0].startswith("Lunch at The Corner Bistro — 54.22 USD, Business dinner and lunch")


def test_with_buttons_asks_about_a_duplicate_with_buttons(erp, tmp_path):
    erp.existing = ExistingExpense("000834", "2026-10-28", "54.22", "USD", "Lunch at The Corner Bistro")
    send, _ = talk_with_buttons(erp, FakeReader(), tmp_path)
    send(photo=True)

    question, choices = send("create")

    assert question.endswith("Create another one?")
    assert choices == [("✅ Create anyway", "yes"), ("✖ Cancel", "no")]
    assert send("yes")[0].startswith("Created expense receipt 000123")


def restaurant(**overrides):
    return FakeReader(**{"merchant": "Hoogan & Beaufort", "total": 183.39, "subtotal": 159.50, "tip_customary": True,
                         "description": "Lunch at Hoogan & Beaufort"} | overrides)


def test_with_buttons_offers_a_tip_on_a_restaurant_bill_without_one(erp, tmp_path):
    send, _ = talk_with_buttons(erp, restaurant(), tmp_path)

    question, choices = send(photo=True)

    assert question == "No tip on this bill. Add one? The percentages are of the 159.50 before tax."
    assert choices == [("✅ No tip · 183.39", "0"), ("15% · 207.32", "15%"), ("18% · 212.10", "18%"),
                       ("20% · 215.29", "20%"), ("✏️ Other…", "other")]


def test_with_buttons_keeps_the_amount_when_there_is_no_tip(erp, tmp_path):
    send, _ = talk_with_buttons(erp, restaurant(), tmp_path)
    send(photo=True)

    assert send("0")[0].startswith("Lunch at Hoogan & Beaufort — 183.39 USD")


def test_with_buttons_adds_a_share_of_the_subtotal_and_shows_it_in_the_summary(erp, tmp_path):
    send, _ = talk_with_buttons(erp, restaurant(), tmp_path)
    send(photo=True)

    assert send("15%")[0].startswith("Lunch at Hoogan & Beaufort + tip 23.93 — 207.32 USD")
    send("create")
    assert (erp.created[0].amount, erp.created[0].description) == ("207.32", "Lunch at Hoogan & Beaufort + tip 23.93")


def test_with_buttons_takes_a_typed_tip_after_other(erp, tmp_path):
    send, _ = talk_with_buttons(erp, restaurant(), tmp_path)
    send(photo=True)

    assert send("other")[0] == "Type the tip, such as 25 or 15%."
    assert "208.39 USD" in send("25")[0]


@pytest.mark.parametrize("answer, amount", [("25", "208.39"), ("15%", "207.32"), ("0", "183.39"), ("non", "183.39")])
def test_by_text_asks_once_for_a_tip_as_an_amount_or_a_share(erp, tmp_path, answer, amount):
    send, _ = talk(erp, restaurant(), tmp_path)

    assert send(KNOWN_PHONE, photo=True) == "No tip on this bill. Reply 0 for none, or the tip, such as 25 or 15%."
    send(KNOWN_PHONE, answer)

    assert erp.created[0].amount == amount


def test_by_text_asks_again_when_the_tip_cannot_be_read(erp, tmp_path):
    send, _ = talk(erp, restaurant(), tmp_path)
    send(KNOWN_PHONE, photo=True)

    assert send(KNOWN_PHONE, "generous").startswith("Please reply with the tip")
    assert erp.created == []


def test_takes_a_share_of_the_total_when_no_subtotal_is_printed(erp, tmp_path):
    send, _ = talk(erp, restaurant(subtotal=None, total=100.0), tmp_path)
    send(KNOWN_PHONE, photo=True)
    send(KNOWN_PHONE, "20%")

    assert erp.created[0].amount == "120.00"


@pytest.mark.parametrize("overrides", [{"tip_shown": True}, {"tip_customary": False}])
def test_does_not_ask_about_a_tip_when_one_is_shown_or_none_is_usual(erp, tmp_path, overrides):
    send, _ = talk(erp, restaurant(**overrides), tmp_path)

    assert send(KNOWN_PHONE, photo=True).startswith("Created expense receipt 000123: 183.39 USD")
