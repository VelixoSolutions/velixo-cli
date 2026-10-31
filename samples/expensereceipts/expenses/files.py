"""Small file helpers shared by the channels and the store."""

from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Callable

import requests

MAX_PHOTO_BYTES = 10 * 1024 * 1024


def download_photo(get: Callable[..., requests.Response], url: str, **kwargs) -> bytes:
    """Downloads a photo with `get` (requests.get or a session's), reading at most MAX_PHOTO_BYTES.

    Raises requests.RequestException when the download fails, and ValueError when the photo is larger."""
    with get(url, timeout=60, stream=True, **kwargs) as response:
        response.raise_for_status()
        photo = response.raw.read(MAX_PHOTO_BYTES + 1, decode_content=True)
    if len(photo) > MAX_PHOTO_BYTES:
        raise ValueError("The photo is larger than 10 MB.")
    return photo


def write_json(path: Path, data) -> None:
    """Writes `data` to a temporary file first, so a crash midway never leaves `path` half written."""
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(data, indent=2))
    os.replace(temporary, path)
