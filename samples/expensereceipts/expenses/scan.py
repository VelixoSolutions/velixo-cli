"""Finds the receipt in a photo, straightens it and crops away the table it lies on."""

from __future__ import annotations

import cv2
import numpy as np

MAX_SIDE = 1600


def straighten_receipt(photo: bytes) -> bytes:
    """Returns the receipt as a JPEG, cropped and seen from straight above. When no receipt-shaped outline is
    found, it returns the whole photo, upright and downsized, so a hard photo still reaches the reader."""
    image = cv2.imdecode(np.frombuffer(photo, np.uint8), cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError("The file is not an image this sample can read.")

    image = _downsize(image)
    corners = _receipt_corners(image)
    result = _warp(image, corners) if corners is not None else image

    ok, encoded = cv2.imencode(".jpg", result, [cv2.IMWRITE_JPEG_QUALITY, 90])
    if not ok:
        raise ValueError("The receipt could not be saved as a JPEG.")
    return encoded.tobytes()


def _downsize(image: np.ndarray) -> np.ndarray:
    height, width = image.shape[:2]
    scale = MAX_SIDE / max(height, width)
    if scale >= 1:
        return image
    return cv2.resize(image, (round(width * scale), round(height * scale)), interpolation=cv2.INTER_AREA)


def _receipt_corners(image: np.ndarray) -> np.ndarray | None:
    """Returns the four corners of the largest bright, roughly rectangular shape: a receipt is lighter than the
    table under it."""
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    blurred = cv2.GaussianBlur(gray, (5, 5), 0)
    _, mask = cv2.threshold(blurred, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_RECT, (15, 15)))

    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return None

    largest = max(contours, key=cv2.contourArea)
    share = cv2.contourArea(largest) / (image.shape[0] * image.shape[1])
    # Under a tenth it is a detail, not the receipt; over nine tenths there is no table to crop away.
    if share < 0.1 or share > 0.9:
        return None

    outline = cv2.approxPolyDP(largest, 0.02 * cv2.arcLength(largest, True), True)
    if len(outline) == 4:
        return outline.reshape(4, 2).astype(np.float32)

    # A crumpled or curled receipt has no clean four-sided outline: use the tightest rotated rectangle.
    return cv2.boxPoints(cv2.minAreaRect(largest)).astype(np.float32)


def _warp(image: np.ndarray, corners: np.ndarray) -> np.ndarray:
    top_left, top_right, bottom_right, bottom_left = _order(corners)
    width = int(max(np.linalg.norm(top_right - top_left), np.linalg.norm(bottom_right - bottom_left)))
    height = int(max(np.linalg.norm(bottom_left - top_left), np.linalg.norm(bottom_right - top_right)))
    target = np.array([[0, 0], [width - 1, 0], [width - 1, height - 1], [0, height - 1]], dtype=np.float32)
    matrix = cv2.getPerspectiveTransform(np.array([top_left, top_right, bottom_right, bottom_left]), target)
    warped = cv2.warpPerspective(image, matrix, (width, height))

    # Receipts are taller than wide; a landscape result means the phone was held sideways.
    return cv2.rotate(warped, cv2.ROTATE_90_CLOCKWISE) if width > height * 1.2 else warped


def _order(corners: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    sums = corners.sum(axis=1)
    differences = np.diff(corners, axis=1).ravel()
    return (
        corners[np.argmin(sums)],
        corners[np.argmin(differences)],
        corners[np.argmax(sums)],
        corners[np.argmax(differences)],
    )
