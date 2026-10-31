import requests
from twilio.request_validator import RequestValidator

import app as expenses_app
from expenses.sms import TwilioSender

URL = "https://example.fly.dev/acumatica/sms"
TOKEN = "0" * 32


class InLine:
    """Runs a background thread's target at once, so the test sees what it sent."""

    def __init__(self, target, daemon=None, args=()):
        self.target, self.args = target, args

    def start(self):
        self.target(*self.args)


def test_replies_when_a_twilio_photo_cannot_be_downloaded(monkeypatch, tmp_path):
    for name, value in {"TWILIO_ACCOUNT_SID": "AC" + "0" * 32, "TWILIO_AUTH_TOKEN": TOKEN, "TWILIO_FROM_NUMBER": "+14388390941",
                        "TWILIO_WEBHOOK_URL": URL, "EXPENSES_STATE": str(tmp_path), "EXPENSES_ERP": "acumatica"}.items():
        monkeypatch.setenv(name, value)
    monkeypatch.delenv("TELEGRAM_BOT_TOKEN", raising=False)
    sent = []
    monkeypatch.setattr(TwilioSender, "send", lambda self, phone, text: sent.append((phone, text)))
    monkeypatch.setattr(expenses_app.threading, "Thread", InLine)

    def unreachable(*args, **kwargs):
        raise requests.ConnectionError("Twilio's media URL did not answer")

    monkeypatch.setattr(expenses_app.requests, "get", unreachable)
    form = {"From": "+15146534118", "Body": "", "NumMedia": "1", "MediaUrl0": "https://api.twilio.com/media/1", "MediaContentType0": "image/jpeg"}
    signature = RequestValidator(TOKEN).compute_signature(URL, form)

    response = expenses_app.create_app().test_client().post("/sms", data=form, headers={"X-Twilio-Signature": signature})

    assert response.status_code == 200
    assert sent == [("+15146534118", "I couldn't download that photo. Please send it again, under 10 MB.")]
