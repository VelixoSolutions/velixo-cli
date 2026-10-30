import cv2
import numpy as np
import pytest

from expenses.phones import normalize_phone
from expenses.scan import black_and_white, straighten_receipt
from tests.receipt_photo import receipt_photo


@pytest.mark.parametrize(
    "stored, texted",
    [
        ("+1 (777) 345-2246", "+17773452246"),
        ("206-321-0942", "+12063210942"),
        ("415.433.1844", "4154331844"),
    ],
)
def test_matches_a_phone_number_however_it_is_written(stored, texted):
    assert normalize_phone(stored) == normalize_phone(texted)


def test_ignores_a_value_that_is_not_a_phone_number():
    assert normalize_phone("ext 12") is None
    assert normalize_phone(None) is None


def decode(jpeg: bytes) -> np.ndarray:
    return cv2.imdecode(np.frombuffer(jpeg, np.uint8), cv2.IMREAD_COLOR)


def test_crops_a_tilted_receipt_to_the_paper_and_stands_it_upright():
    result = decode(straighten_receipt(receipt_photo(angle_degrees=12)))
    height, width = result.shape[:2]

    assert height > width, "a receipt comes back portrait"
    assert 580 < height < 720 and 360 < width < 480, "the crop is the paper, not the table"
    assert result.mean() > 200, "the table around the receipt is cut away"


def test_finds_a_receipt_on_a_light_table_by_its_edges():
    result = decode(straighten_receipt(receipt_photo(angle_degrees=8, table=(200, 205, 210))))
    height, width = result.shape[:2]

    assert 580 < height < 720 and 360 < width < 480, "the crop is the paper, not the whole light table"


def test_turns_the_receipt_into_a_black_and_white_scan():
    scan = cv2.imdecode(np.frombuffer(black_and_white(straighten_receipt(receipt_photo())), np.uint8), cv2.IMREAD_UNCHANGED)

    assert scan.ndim == 2, "one channel: no colour"
    assert set(np.unique(scan)) <= {0, 255}, "only black and white"
    assert (scan == 255).mean() > 0.8, "mostly white paper"
    assert (scan == 0).sum() > 500, "the print is kept"


def test_turns_a_receipt_photographed_sideways_upright():
    result = decode(straighten_receipt(receipt_photo(angle_degrees=88)))
    height, width = result.shape[:2]

    assert height > width


def test_keeps_a_photo_without_a_receipt_shape_whole():
    noise = np.random.default_rng(1).integers(0, 255, (300, 400, 3), np.uint8)
    ok, encoded = cv2.imencode(".jpg", noise)

    result = decode(straighten_receipt(encoded.tobytes()))

    assert result.shape[:2] == (300, 400)


def test_refuses_a_file_that_is_not_an_image():
    with pytest.raises(ValueError):
        straighten_receipt(b"not an image")
