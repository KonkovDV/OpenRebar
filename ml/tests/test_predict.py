from __future__ import annotations

import numpy as np

from src.segmentation.predict import mask_to_polygons


def test_mask_to_polygons_closing_merges_single_pixel_gap() -> None:
    mask = np.zeros((30, 30), dtype=np.uint8)
    mask[5:25, 5:25] = 1
    mask[14, 5:25] = 0

    zones = mask_to_polygons(mask, min_area=20)

    assert len(zones) == 1
    assert zones[0]["class_id"] == 1
    x, y, w, h = zones[0]["bbox"]
    assert x <= 6
    assert y <= 6
    assert w >= 18
    assert h >= 18