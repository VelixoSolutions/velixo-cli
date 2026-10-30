"""Reads a receipt with a vision model through OpenRouter: what was bought, when, for how much, and which expense
item and project fit."""

from __future__ import annotations

import base64
import json
import os
from dataclasses import dataclass

import requests

from .erp import Choice

OPENROUTER_URL = "https://openrouter.ai/api/v1/chat/completions"
DEFAULT_MODEL = "anthropic/claude-opus-5.5"

INSTRUCTIONS = """You read photos of receipts that employees text to their company's expense system.

Return what the receipt shows: the merchant, the purchase date, the total actually paid (tip included) and its
currency. Use null for anything you cannot read with confidence; never guess an amount or a date.

Choose the expense item from the list that fits the purchase. Use the merchant, what was bought and the
employee's own words. Return null only when no item in the list could reasonably fit.

Choose a project only when the employee's words name one or clearly point to one, by its ID or its description.
Copy the words they used into project_hint, even when no project in the list matches them. When they say nothing
about a project, return null for both.

Write a short description for the expense, under 60 characters, such as "Client dinner at Nobu" or "Taxi to
airport". Include the employee's notes when they add something the receipt does not show."""

FIELDS = ["merchant", "date", "total", "currency", "expense_item", "project", "project_hint", "description"]


@dataclass
class ReceiptReading:
    merchant: str | None
    date: str | None
    total: float | None
    currency: str | None
    expense_item: str | None
    project: str | None
    project_hint: str | None
    description: str


class ReceiptNotRead(Exception):
    pass


class ReceiptReader:
    def __init__(self, api_key: str | None = None, model: str | None = None, session: requests.Session | None = None):
        self.api_key = api_key or os.environ.get("OPENROUTER_API_KEY")
        self.model = model or os.environ.get("OPENROUTER_MODEL") or DEFAULT_MODEL
        self.session = session or requests.Session()

    def read(self, receipt_jpeg: bytes, notes: str, expense_items: list[Choice], projects: list[Choice]) -> ReceiptReading:
        if not self.api_key:
            raise ReceiptNotRead("OPENROUTER_API_KEY is not set.")

        lists = {
            "expense_items": [{"id": item.id, "description": item.description} for item in expense_items],
            "projects": [{"id": project.id, "description": project.description} for project in projects],
        }
        image = "data:image/jpeg;base64," + base64.standard_b64encode(receipt_jpeg).decode()
        response = self.session.post(
            OPENROUTER_URL,
            headers={"Authorization": f"Bearer {self.api_key}", "X-Title": "Velixo CLI expense receipts sample"},
            json={
                "model": self.model,
                "messages": [
                    {"role": "system", "content": INSTRUCTIONS},
                    {
                        "role": "user",
                        "content": [
                            {"type": "image_url", "image_url": {"url": image}},
                            {"type": "text", "text": f"Lists from the ERP:\n{json.dumps(lists)}\n\nThe employee's words: {notes or '(none)'}"},
                        ],
                    },
                ],
                "response_format": {
                    "type": "json_schema",
                    "json_schema": {"name": "receipt", "strict": True, "schema": _schema(expense_items, projects)},
                },
            },
            timeout=120,
        )

        if response.status_code != 200:
            raise ReceiptNotRead(f"OpenRouter answered {response.status_code}: {response.text[:300]}")
        try:
            content = response.json()["choices"][0]["message"]["content"]
            fields = json.loads(content)
        except (KeyError, IndexError, TypeError, json.JSONDecodeError) as error:
            raise ReceiptNotRead("The reader returned no receipt.") from error
        return ReceiptReading(**{name: fields.get(name) for name in FIELDS} | {"description": fields.get("description") or "Expense"})


def _schema(expense_items: list[Choice], projects: list[Choice]) -> dict:
    def one_of(ids: list[str]) -> dict:
        return {"anyOf": [{"type": "string", "enum": ids}, {"type": "null"}]} if ids else {"type": "null"}

    nullable_string = {"type": ["string", "null"]}
    return {
        "type": "object",
        "additionalProperties": False,
        "required": FIELDS,
        "properties": {
            "merchant": nullable_string,
            "date": {"type": ["string", "null"], "description": "YYYY-MM-DD"},
            "total": {"type": ["number", "null"]},
            "currency": {"type": ["string", "null"], "description": "ISO 4217 code, such as USD"},
            "expense_item": one_of([item.id for item in expense_items]),
            "project": one_of([project.id for project in projects]),
            "project_hint": nullable_string,
            "description": {"type": "string"},
        },
    }
