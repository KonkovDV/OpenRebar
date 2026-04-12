"""
OpenRebar — Isoline segmentation dataset.

Loads paired (image, mask) training data for U-Net training.
Expected directory layout:
    data/train/images/   — input PNG/JPG isolines
    data/train/masks/    — class index masks (same filename, PNG)
    data/val/images/
    data/val/masks/
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

import numpy as np
import torch
from torch.utils.data import Dataset

try:
    import cv2
except ImportError:
    cv2 = None  # type: ignore[assignment]


class IsolineSegmentationDataset(Dataset):
    """PyTorch dataset for isoline image segmentation training."""

    def __init__(
        self,
        images_dir: Path,
        masks_dir: Path,
        input_size: tuple[int, int] = (512, 512),
        augment: bool = False,
    ) -> None:
        self.images_dir = Path(images_dir)
        self.masks_dir = Path(masks_dir)
        self.input_size = input_size
        self.augment = augment

        self.image_paths = sorted(
            p for p in self.images_dir.iterdir()
            if p.suffix.lower() in {".png", ".jpg", ".jpeg", ".bmp"}
        )

        if not self.image_paths:
            raise FileNotFoundError(f"No images found in {images_dir}")

    def __len__(self) -> int:
        return len(self.image_paths)

    def __getitem__(self, idx: int) -> dict[str, Any]:
        img_path = self.image_paths[idx]
        mask_path = self.masks_dir / img_path.with_suffix(".png").name

        if cv2 is None:
            raise ImportError("opencv-python-headless is required for training")

        img = cv2.imread(str(img_path))
        if img is None:
            raise FileNotFoundError(f"Cannot read image: {img_path}")

        mask = cv2.imread(str(mask_path), cv2.IMREAD_GRAYSCALE)
        if mask is None:
            raise FileNotFoundError(f"Cannot read mask: {mask_path}")

        img = cv2.resize(img, self.input_size)
        mask = cv2.resize(mask, self.input_size, interpolation=cv2.INTER_NEAREST)

        if self.augment:
            img, mask = self._apply_augmentation(img, mask)

        # Normalize to [0, 1] and convert to tensor
        img_tensor = torch.from_numpy(img).permute(2, 0, 1).float() / 255.0
        mask_tensor = torch.from_numpy(mask).long()

        return {"image": img_tensor, "mask": mask_tensor, "path": str(img_path)}

    @staticmethod
    def _apply_augmentation(
        img: np.ndarray, mask: np.ndarray
    ) -> tuple[np.ndarray, np.ndarray]:
        """Simple augmentation: random horizontal flip + random rotation."""
        if np.random.random() > 0.5:
            img = np.fliplr(img).copy()
            mask = np.fliplr(mask).copy()

        if np.random.random() > 0.5:
            img = np.flipud(img).copy()
            mask = np.flipud(mask).copy()

        # Random 90-degree rotation
        k = np.random.randint(0, 4)
        if k > 0:
            img = np.rot90(img, k).copy()
            mask = np.rot90(mask, k).copy()

        return img, mask
