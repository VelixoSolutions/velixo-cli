import json

import pytest

from expenses.telegram import TelegramBot
from tests.receipt_photo import receipt_photo

CHAT = 4242
USER = 777


class FakeResponse:
    def __init__(self, payload=None, content=b""):
        self.payload, self.content = payload, content
        self.raw = self

    def raise_for_status(self):
        pass

    def json(self):
        return self.payload

    def read(self, limit, decode_content=True):
        return self.content[:limit]

    def __enter__(self):
        return self

    def __exit__(self, *args):
        pass


class FakeTelegram:
    """Answers the Bot API calls the bot makes, and keeps every message it sends."""

    def __init__(self):
        self.sent = []
        self.photo = receipt_photo()

    def post(self, url, json, timeout):
        assert url.endswith("/sendMessage")
        self.sent.append(json)
        return FakeResponse()

    def get(self, url, params=None, timeout=None, stream=False):
        if url.endswith("/getFile"):
            return FakeResponse({"result": {"file_path": f"photos/{params['file_id']}.jpg"}})
        return FakeResponse(content=self.photo)


@pytest.fixture
def bot(tmp_path):
    telegram = FakeTelegram()
    handled = []

    def handle(phone, text, photos, reply):
        handled.append((phone, text, photos))
        reply("Created expense receipt 000123.")

    def receive(message):
        TelegramBot("token", tmp_path, session=telegram).receive({"message": {"chat": {"id": CHAT}, "from": {"id": USER}, **message}}, handle)

    return receive, telegram, handled


def share_phone(receive, user=USER, phone="17773452246"):
    receive({"contact": {"phone_number": phone, "user_id": user}})


def test_asks_for_the_phone_number_with_a_button_before_anything_else(bot):
    receive, telegram, handled = bot

    receive({"photo": [{"file_id": "small"}, {"file_id": "large"}]})

    assert handled == []
    assert telegram.sent[-1]["reply_markup"]["keyboard"][0][0]["request_contact"] is True


def test_hands_the_largest_photo_and_its_caption_to_the_conversation_once_the_number_is_shared(bot):
    receive, telegram, handled = bot
    share_phone(receive)

    receive({"photo": [{"file_id": "small"}, {"file_id": "large"}], "caption": "Lunch with the Marlin team"})

    phone, text, photos = handled[0]
    assert (phone, text) == ("+17773452246", "Lunch with the Marlin team")
    assert photos == [telegram.photo]
    assert telegram.sent[-1]["chat_id"] == CHAT and telegram.sent[-1]["text"] == "Created expense receipt 000123."


def test_passes_a_text_answer_on_without_photos(bot):
    receive, _, handled = bot
    share_phone(receive)

    receive({"text": "2"})

    assert handled == [("+17773452246", "2", [])]


def test_refuses_a_contact_that_is_not_the_users_own(bot):
    receive, telegram, handled = bot

    share_phone(receive, user=USER + 1)
    receive({"text": "hello"})

    assert handled == []
    assert "your own phone number" in telegram.sent[0]["text"]


def test_remembers_the_number_across_restarts(bot, tmp_path):
    receive, _, _ = bot
    share_phone(receive)

    assert json.loads((tmp_path / "telegram-phones.json").read_text()) == {str(CHAT): "+17773452246"}
