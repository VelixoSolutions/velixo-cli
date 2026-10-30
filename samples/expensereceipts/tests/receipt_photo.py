"""Draws a receipt and photographs it, tilted, on a dark table: the kind of picture an employee texts."""

from __future__ import annotations

import io

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

LINES = [
    "THE CORNER BISTRO",
    "1200 Market St, San Francisco",
    "2026-10-28  19:42",
    "",
    "2 x Lunch special      38.00",
    "1 x Sparkling water     4.50",
    "",
    "Subtotal               42.50",
    "Tax                     3.72",
    "Tip                     8.00",
    "TOTAL USD              54.22",
    "",
    "Thank you!",
]


def receipt_photo(angle_degrees: float = 12, table: tuple[int, int, int] = (40, 55, 75)) -> bytes:
    paper = Image.new("RGB", (420, 640), "white")
    draw = ImageDraw.Draw(paper)
    font = ImageFont.load_default(size=22)
    for index, line in enumerate(LINES):
        draw.text((24, 30 + index * 44), line, fill="black", font=font)

    receipt = cv2.cvtColor(np.array(paper), cv2.COLOR_RGB2BGR)
    table = np.full((1100, 1100, 3), table, np.uint8)

    height, width = receipt.shape[:2]
    corners = np.float32([[0, 0], [width, 0], [width, height], [0, height]])
    center = np.float32([550, 550])
    radians = np.radians(angle_degrees)
    rotation = np.float32([[np.cos(radians), -np.sin(radians)], [np.sin(radians), np.cos(radians)]])
    placed = (corners - [width / 2, height / 2]) @ rotation.T + center
    placed[0] += [25, 10]  # a little perspective, as from a hand-held phone

    matrix = cv2.getPerspectiveTransform(corners, placed.astype(np.float32))
    warped = cv2.warpPerspective(receipt, matrix, (1100, 1100))
    mask = cv2.warpPerspective(np.full((height, width), 255, np.uint8), matrix, (1100, 1100))
    table[mask > 0] = warped[mask > 0]

    buffer = io.BytesIO()
    Image.fromarray(cv2.cvtColor(table, cv2.COLOR_BGR2RGB)).save(buffer, format="JPEG", quality=90)
    return buffer.getvalue()


if __name__ == "__main__":
    import sys

    sys.stdout.buffer.write(receipt_photo())
