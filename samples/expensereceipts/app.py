"""Expense receipts by text message: an employee texts a photo of a receipt, and the app creates the expense in
Acumatica or Sage Intacct through the Velixo CLI and texts back a link to it.

Run it with `python app.py`. POST /sms is the Twilio webhook; / is a simulator page that needs no Twilio account."""

from __future__ import annotations

import base64
import os
import threading
from pathlib import Path

import requests
from flask import Flask, Response, abort, jsonify, request, send_from_directory

from expenses.conversation import Conversation, Store
from expenses.erp import Acumatica, AcumaticaSettings, SageIntacct, SageIntacctSettings
from expenses.reader import ReceiptReader
from expenses.sms import SimulatorOutbox, TwilioSender
from expenses.telegram import TelegramBot
from expenses.velixo import Velixo

HERE = Path(__file__).resolve().parent
MAX_PHOTO_BYTES = 10 * 1024 * 1024


def create_app() -> Flask:
    erp_name = os.environ.get("EXPENSES_ERP", "acumatica").lower()
    velixo = Velixo(connection=os.environ.get("VELIXO_CONNECTION"), binary=os.environ.get("VELIXO_BIN", "velixo"))
    base_currency = os.environ.get("EXPENSES_BASE_CURRENCY", "USD")

    if erp_name == "acumatica":
        erp = Acumatica(velixo, AcumaticaSettings(url=os.environ.get("ACUMATICA_URL"), company=os.environ.get("ACUMATICA_COMPANY")))
    elif erp_name in {"sageintacct", "sage"}:
        erp = SageIntacct(velixo, SageIntacctSettings(base_currency=base_currency, url_template=os.environ.get("SAGE_EXPENSE_URL_TEMPLATE")))
    else:
        raise SystemExit(f"EXPENSES_ERP must be acumatica or sageintacct, not {erp_name}.")

    state = Path(os.environ.get("EXPENSES_STATE", HERE / ".state"))
    conversation = Conversation(erp, ReceiptReader(), Store(state), base_currency)
    outbox = SimulatorOutbox()
    twilio = _twilio_sender()
    telegram = TelegramBot(os.environ["TELEGRAM_BOT_TOKEN"], state) if os.environ.get("TELEGRAM_BOT_TOKEN") else None

    app = Flask(__name__, static_folder=str(HERE / "static"))

    def in_background(phone: str, text: str, photos, send) -> None:
        """Handles the message on its own thread; `photos` returns the photo bytes, so downloads happen there too."""
        def handle():
            conversation.handle(phone, text, photos(), lambda reply: send(phone, reply))

        threading.Thread(target=handle, daemon=True).start()

    @app.post("/sms")
    def twilio_webhook():
        if twilio is None:
            abort(503, "Set TWILIO_ACCOUNT_SID, TWILIO_AUTH_TOKEN and TWILIO_FROM_NUMBER to receive texts.")
        _check_twilio_signature(twilio.auth_token)

        urls = [
            request.form[f"MediaUrl{index}"]
            for index in range(int(request.form.get("NumMedia", "0")))
            if request.form.get(f"MediaContentType{index}", "").startswith("image/")
        ]
        in_background(request.form["From"], request.form.get("Body", ""), lambda: [_download(url, twilio) for url in urls], twilio.send)
        return Response("<Response/>", mimetype="application/xml")

    @app.post("/telegram")
    def telegram_webhook():
        if telegram is None:
            abort(503, "Set TELEGRAM_BOT_TOKEN and TELEGRAM_WEBHOOK_SECRET to receive Telegram messages.")
        secret = os.environ.get("TELEGRAM_WEBHOOK_SECRET", "")
        if not secret or request.headers.get("X-Telegram-Bot-Api-Secret-Token") != secret:
            abort(403)
        update = request.get_json(silent=True) or {}
        threading.Thread(target=telegram.receive, args=(update, conversation.handle), daemon=True).start()
        return jsonify(ok=True)

    if twilio is not None or telegram is not None:
        return app

    # The simulator lets anyone send as any phone number, so it runs only while neither Twilio nor Telegram is set up.
    @app.get("/")
    def simulator():
        return send_from_directory(app.static_folder, "simulator.html")

    @app.post("/simulator/messages")
    def simulator_send():
        phone = request.form.get("phone", "").strip()
        text = request.form.get("text", "")
        upload = request.files.get("photo")
        photo = upload.read(MAX_PHOTO_BYTES + 1) if upload else None
        if not phone or (photo and len(photo) > MAX_PHOTO_BYTES):
            abort(400)

        preview = f"data:{upload.mimetype};base64,{base64.b64encode(photo).decode()}" if photo else None
        outbox.incoming(phone, text, preview)
        in_background(phone, text, lambda: [photo] if photo else [], outbox.send)
        return jsonify(ok=True)

    @app.get("/simulator/messages")
    def simulator_history():
        return jsonify(outbox.history(request.args.get("phone", "")))

    return app


def _twilio_sender() -> TwilioSender | None:
    sid, token, number = (os.environ.get(name) for name in ("TWILIO_ACCOUNT_SID", "TWILIO_AUTH_TOKEN", "TWILIO_FROM_NUMBER"))
    return TwilioSender(sid, token, number) if sid and token and number else None


def _check_twilio_signature(auth_token: str) -> None:
    from twilio.request_validator import RequestValidator

    url = os.environ.get("TWILIO_WEBHOOK_URL") or request.url
    if not RequestValidator(auth_token).validate(url, request.form, request.headers.get("X-Twilio-Signature", "")):
        abort(403)


def _download(url: str, twilio: TwilioSender) -> bytes:
    """Twilio serves a message's photos only to the account that received it."""
    with requests.get(url, auth=(twilio.account_sid, twilio.auth_token), timeout=60, stream=True) as response:
        response.raise_for_status()
        photo = response.raw.read(MAX_PHOTO_BYTES + 1, decode_content=True)
    if len(photo) > MAX_PHOTO_BYTES:
        raise ValueError("The photo is larger than 10 MB.")
    return photo


if __name__ == "__main__":
    create_app().run(host=os.environ.get("HOST", "127.0.0.1"), port=int(os.environ.get("PORT", "8090")))
