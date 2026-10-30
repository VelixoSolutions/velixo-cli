"""Finds the receipt in a photo, straightens it and crops away the table it lies on, and turns it into a black and
white scan."""

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


def black_and_white(receipt: bytes) -> bytes:
    """Returns the receipt as a black and white PNG, like a scan: dark print on white, with shadows and the
    paper's tint evened out. Each pixel is compared with its neighbourhood, so a shadow across the paper does
    not turn the print around it black."""
    image = cv2.imdecode(np.frombuffer(receipt, np.uint8), cv2.IMREAD_GRAYSCALE)
    if image is None:
        raise ValueError("The file is not an image this sample can read.")

    # The outline found is the paper's edge, give or take a few pixels of table, which would print as a dotted frame.
    height, width = image.shape
    image = image[height * 2 // 100 : height * 98 // 100, width * 2 // 100 : width * 98 // 100]

    smoothed = cv2.medianBlur(image, 3)
    block = max(15, (max(image.shape) // 40) | 1)
    scan = cv2.adaptiveThreshold(smoothed, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C, cv2.THRESH_BINARY, block, 12)

    ok, encoded = cv2.imencode(".png", scan)
    if not ok:
        raise ValueError("The scan could not be saved as a PNG.")
    return encoded.tobytes()


def _downsize(image: np.ndarray) -> np.ndarray:
    height, width = image.shape[:2]
    scale = MAX_SIDE / max(height, width)
    if scale >= 1:
        return image
    return cv2.resize(image, (round(width * scale), round(height * scale)), interpolation=cv2.INTER_AREA)


def _receipt_corners(image: np.ndarray) -> np.ndarray | None:
    """Returns the four corners of the receipt: the largest four-sided outline among the photo's edges; else the
    white, colourless paper, which also works when the receipt runs off the photo; else the largest shape lighter
    than the table."""
    for find in (_outline_from_edges, _outline_from_paper, _outline_from_brightness):
        corners = find(image)
        if corners is not None:
            return corners
    return None


def _outline_from_edges(image: np.ndarray) -> np.ndarray | None:
    gray = cv2.GaussianBlur(cv2.cvtColor(image, cv2.COLOR_BGR2GRAY), (5, 5), 0)
    edges = cv2.dilate(cv2.Canny(gray, 50, 150), cv2.getStructuringElement(cv2.MORPH_RECT, (5, 5)))
    contours, _ = cv2.findContours(edges, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    area = image.shape[0] * image.shape[1]
    for contour in sorted(contours, key=cv2.contourArea, reverse=True)[:5]:
        # Under a tenth it is a detail, not the receipt. A four-sided outline may nearly fill the photo.
        if cv2.contourArea(contour) < 0.1 * area:
            return None
        outline = cv2.approxPolyDP(contour, 0.02 * cv2.arcLength(contour, True), True)
        if len(outline) == 4 and cv2.contourArea(outline) < 0.98 * area:
            return outline.reshape(4, 2).astype(np.float32)
    return None


def _outline_from_paper(image: np.ndarray) -> np.ndarray | None:
    """Finds the receipt as the largest white, nearly colourless area: a wooden or coloured table is not. Rows much
    wider than the receipt, such as a keyboard touching its top, are left out, and a straight line is fitted to each
    side, so a receipt photographed at an angle keeps its trapezoid shape."""
    hsv = cv2.cvtColor(image, cv2.COLOR_BGR2HSV)
    white = ((hsv[..., 1] < 30) & (hsv[..., 2] > 170)).astype(np.uint8) * 255
    white = cv2.morphologyEx(white, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_RECT, (9, 9)))
    count, labels, stats, _ = cv2.connectedComponentsWithStats(white)
    if count < 2:
        return None

    paper = labels == 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    widths = paper.sum(axis=1)
    rows = np.flatnonzero((widths > 0) & (widths < 1.5 * np.median(widths[widths > 0])))
    # A white area that covers most of the photo is the table, not a receipt on it.
    if rows.size < 0.2 * image.shape[0] or np.median(widths[rows]) > 0.8 * image.shape[1]:
        return None

    lefts = np.array([np.argmax(paper[row]) for row in rows])
    rights = np.array([paper.shape[1] - 1 - np.argmax(paper[row][::-1]) for row in rows])
    left, right = np.polyfit(rows, lefts, 1), np.polyfit(rows, rights, 1)
    # The rows left out at the top may still hold the start of the receipt, such as the merchant's name.
    top = max(0, rows.min() - image.shape[0] // 50)
    bottom = rows.max()
    return np.float32([
        [np.polyval(left, top), top], [np.polyval(right, top), top],
        [np.polyval(right, bottom), bottom], [np.polyval(left, bottom), bottom],
    ])


def _outline_from_brightness(image: np.ndarray) -> np.ndarray | None:
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
