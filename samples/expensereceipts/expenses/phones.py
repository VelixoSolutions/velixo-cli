"""Phone numbers are stored in many formats ("+1 (777) 345-2246", "206-321-0942"), so they are compared as digits."""

from __future__ import annotations

import re


def normalize_phone(value: str | None) -> str | None:
    """Returns the last ten digits of a phone number, which is the number without its country code in North
    America, or None when it holds fewer than seven digits."""
    if not value:
        return None
    digits = re.sub(r"\D", "", value)
    if len(digits) < 7:
        return None
    return digits[-10:]
