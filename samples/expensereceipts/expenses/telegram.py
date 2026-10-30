"""Telegram as a second way in: the bot finds the employee by the phone number the user shares with it, then
hands each message to the same conversation as a text message."""

from __future__ import annotations

import json
import os
import threading
from pathlib import Path
from typing import Callable

import requests

API = "https://api.telegram.org"
MAX_PHOTO_BYTES = 10 * 1024 * 1024
SHARE_BUTTON = "Share my phone number"

Handle = Callable[..., None]


class TelegramBot:
    def __init__(self, token: str, state: Path, session: requests.Session | None = None):
        self.token = token
        self.session = session or requests.Session()
        self._phones_file = state / "telegram-phones.json"
        self._lock = threading.Lock()

    def receive(self, update: dict, handle: Handle) -> None:
        """Handles one update from the webhook: a shared phone number, a tap on a button, or a message for the
        conversation. A tap reaches the conversation as the answer the button stands for."""
        tap = update.get("callback_query")
        if tap:
            self._call("answerCallbackQuery", {"callback_query_id": tap["id"]})
            message = tap.get("message") or {}
            chat = (message.get("chat") or {}).get("id")
            if chat is None:
                return
            # The buttons go once tapped, so an old question cannot be answered twice.
            self._call("editMessageReplyMarkup", {"chat_id": chat, "message_id": message["message_id"], "reply_markup": {"inline_keyboard": []}})
            phone = self._phone(chat)
            if phone is not None:
                handle(phone, tap.get("data") or "", [], lambda reply: self.send(chat, reply), lambda text, choices: self.send(chat, text, choices=choices))
            return

        message = update.get("message") or {}
        chat = (message.get("chat") or {}).get("id")
        if chat is None:
            return

        contact = message.get("contact")
        if contact:
            if contact.get("user_id") != (message.get("from") or {}).get("id"):
                self.send(chat, "Please share your own phone number, with the button below.", ask_for_phone=True)
                return
            self._remember(chat, contact["phone_number"])
            self.send(chat, "Thanks. Now send me a photo of a receipt, and add the project or a note if you like.")
            return

        phone = self._phone(chat)
        if phone is None:
            self.send(chat, "To find you in the ERP, I need your phone number. Tap the button below, then send the receipt again.", ask_for_phone=True)
            return

        try:
            photos = [self._download(file) for file in _image_files(message)]
        except (requests.RequestException, ValueError, KeyError):
            self.send(chat, "I couldn't download that photo. Please send it again, under 10 MB.")
            return
        handle(
            phone,
            message.get("text") or message.get("caption") or "",
            photos,
            lambda reply: self.send(chat, reply),
            lambda text, choices: self.send(chat, text, choices=choices),
        )

    def send(self, chat: int, text: str, ask_for_phone: bool = False, choices: list[tuple[str, str]] | None = None) -> None:
        """Sends a message; `choices`, each a label and the answer it sends back, become buttons under it."""
        if choices:
            markup = {"inline_keyboard": [[{"text": label, "callback_data": value[:64]}] for label, value in choices]}
        elif ask_for_phone:
            markup = {"keyboard": [[{"text": SHARE_BUTTON, "request_contact": True}]], "one_time_keyboard": True, "resize_keyboard": True}
        else:
            markup = {"remove_keyboard": True}
        self._call("sendMessage", {"chat_id": chat, "text": text, "reply_markup": markup})

    def _call(self, method: str, body: dict) -> None:
        self.session.post(f"{API}/bot{self.token}/{method}", json=body, timeout=30).raise_for_status()

    def _download(self, file_id: str) -> bytes:
        found = self.session.get(f"{API}/bot{self.token}/getFile", params={"file_id": file_id}, timeout=30)
        found.raise_for_status()
        path = found.json()["result"]["file_path"]
        with self.session.get(f"{API}/file/bot{self.token}/{path}", timeout=60, stream=True) as response:
            response.raise_for_status()
            photo = response.raw.read(MAX_PHOTO_BYTES + 1, decode_content=True)
        if len(photo) > MAX_PHOTO_BYTES:
            raise ValueError("The photo is larger than 10 MB.")
        return photo

    def _phone(self, chat: int) -> str | None:
        with self._lock:
            return self._load().get(str(chat))

    def _remember(self, chat: int, phone: str) -> None:
        with self._lock:
            phones = self._load()
            phones[str(chat)] = phone if phone.startswith("+") else f"+{phone}"
            temporary = self._phones_file.with_suffix(".tmp")
            temporary.write_text(json.dumps(phones, indent=2))
            os.replace(temporary, self._phones_file)

    def _load(self) -> dict:
        return json.loads(self._phones_file.read_text()) if self._phones_file.exists() else {}


def _image_files(message: dict) -> list[str]:
    """Returns the file id of the photo's largest size, or of an image sent as a file."""
    if message.get("photo"):
        return [message["photo"][-1]["file_id"]]
    document = message.get("document") or {}
    if document.get("mime_type", "").startswith("image/"):
        return [document["file_id"]]
    return []
