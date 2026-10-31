"""Where replies go: back to the phone through Twilio, or to the simulator page."""

from __future__ import annotations

import threading
from dataclasses import dataclass, field
from datetime import datetime


@dataclass
class TwilioSender:
    account_sid: str
    auth_token: str
    from_number: str

    def send(self, phone: str, text: str) -> None:
        from twilio.rest import Client

        Client(self.account_sid, self.auth_token).messages.create(to=phone, from_=self.from_number, body=text)


KEEP_MESSAGES = 50


@dataclass
class SimulatorOutbox:
    """Keeps the last KEEP_MESSAGES messages of each simulated phone, for the simulator page to show. Each photo
    is kept as a data URL, so keeping every message would grow without limit."""

    messages: dict[str, list[dict]] = field(default_factory=dict)
    _lock: threading.Lock = field(default_factory=threading.Lock)

    def incoming(self, phone: str, text: str, photo: str | None = None) -> None:
        self._add(phone, {"from": "employee", "text": text, "photo": photo})

    def send(self, phone: str, text: str) -> None:
        self._add(phone, {"from": "app", "text": text})

    def history(self, phone: str) -> list[dict]:
        with self._lock:
            return list(self.messages.get(phone, []))

    def _add(self, phone: str, message: dict) -> None:
        with self._lock:
            messages = self.messages.setdefault(phone, [])
            messages.append({**message, "at": datetime.now().strftime("%H:%M")})
            del messages[:-KEEP_MESSAGES]
