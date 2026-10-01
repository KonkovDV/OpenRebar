"""
Inference pipeline: image → segmentation mask → polygons.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

import cv2
import numpy as np
import torch

from .model import IsolineUNet


def _decode_image(path: Path, flags: int) -> np.ndarray | None:
    try:
        encoded = np.fromfile(path, dtype=np.uint8)
    except OSError:
        return None

    if encoded.size == 0:
        return None

    return cv2.imdecode(encoded, flags)


def load_model(
    model_path: Path,
    num_classes: int = 8,
    device: str = "cpu",
) -> IsolineUNet:
    """Load a trained U-Net model from a .pt checkpoint."""
    model = IsolineUNet(num_classes=num_classes)
    state = torch.load(model_path, map_location=device, weights_only=True)
    model.load_state_dict(state)
    model.eval()
    model.to(device)
    return model


def predict_mask(
    model: IsolineUNet,
    image_path: Path,
    device: str = "cpu",
    input_size: tuple[int, int] = (512, 512),
) -> np.ndarray:
    """
    Run inference on a single image.

    Returns:
        mask: H×W array of class indices (0 = background).
    """
    img = _decode_image(image_path, cv2.IMREAD_COLOR)
    if img is None:
        raise FileNotFoundError(f"Cannot read image: {image_path}")

    original_h, original_w = img.shape[:2]

    # Preprocess: resize, normalize, to tensor
    resized = cv2.resize(img, input_size)
    tensor = torch.from_numpy(resized).permute(2, 0, 1).float() / 255.0
    tensor = tensor.unsqueeze(0).to(device)

    with torch.no_grad():
        logits = model(tensor)  # [1, C, H, W]
        mask = logits.argmax(dim=1).squeeze(0).cpu().numpy()  # [H, W]

    # Resize mask back to original resolution
    mask = cv2.resize(mask.astype(np.uint8), (original_w, original_h), interpolation=cv2.INTER_NEAREST)
    return mask


def mask_to_polygons(
    mask: np.ndarray,
    min_area: float = 1000.0,
) -> list[dict[str, Any]]:
    """
    Convert a segmentation mask into a list of polygon zones.

    Returns:
        List of dicts with keys:
            - class_id: int (zone class)
            - polygon: list of (x, y) tuples (boundary vertices)
            - area: float (polygon area in pixels)
            - bbox: (x, y, w, h) bounding box
    """
    zones: list[dict[str, Any]] = []

    unique_classes = np.unique(mask)
    for class_id in unique_classes:
        if class_id == 0:  # Skip background
            continue

        # Binary mask for this class
        binary = (mask == class_id).astype(np.uint8) * 255

        # Morphological cleanup
        kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (5, 5))
        binary = cv2.morphologyEx(binary, cv2.MORPH_CLOSE, kernel)
        binary = cv2.morphologyEx(binary, cv2.MORPH_OPEN, kernel)

        # Find contours
        contours, _ = cv2.findContours(binary, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        for contour in contours:
            area = cv2.contourArea(contour)
            if area < min_area:
                continue

            # Simplify polygon (Douglas-Peucker)
            epsilon = 0.01 * cv2.arcLength(contour, True)
            approx = cv2.approxPolyDP(contour, epsilon, True)

            polygon = [(int(pt[0][0]), int(pt[0][1])) for pt in approx]

            if len(polygon) < 3:
                continue

            x, y, w, h = cv2.boundingRect(contour)

            zones.append({
                "class_id": int(class_id),
                "polygon": polygon,
                "area": float(area),
                "bbox": (x, y, w, h),
            })

    return zones


def segment_isoline_image(
    model: IsolineUNet,
    image_path: Path,
    device: str = "cpu",
    min_area: float = 1000.0,
) -> list[dict[str, Any]]:
    """Full pipeline: image → model → mask → polygons."""
    mask = predict_mask(model, image_path, device)
    return mask_to_polygons(mask, min_area)
