"""
OpenRebar — Model evaluation metrics.

Computes per-class IoU, mean IoU, pixel accuracy, and confusion matrix
for segmentation model evaluation.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

import numpy as np
import torch
from torch.utils.data import DataLoader

from ..segmentation.model import IsolineUNet
from ..segmentation.predict import load_model
from .dataset import IsolineSegmentationDataset


@dataclass
class EvaluationResult:
    """Evaluation metrics for a segmentation model."""
    mean_iou: float
    pixel_accuracy: float
    per_class_iou: dict[int, float] = field(default_factory=dict)
    confusion_matrix: np.ndarray = field(default_factory=lambda: np.zeros((1, 1)))
    num_samples: int = 0


def compute_iou(pred: np.ndarray, target: np.ndarray, num_classes: int) -> dict[int, float]:
    """Compute per-class Intersection over Union."""
    ious: dict[int, float] = {}
    for cls in range(num_classes):
        pred_mask = pred == cls
        target_mask = target == cls
        intersection = np.logical_and(pred_mask, target_mask).sum()
        union = np.logical_or(pred_mask, target_mask).sum()
        if union > 0:
            ious[cls] = float(intersection / union)
    return ious


def evaluate(
    model_path: Path,
    data_dir: Path,
    num_classes: int = 8,
    input_size: tuple[int, int] = (512, 512),
    batch_size: int = 4,
    device: str = "cpu",
) -> EvaluationResult:
    """Evaluate segmentation model on a dataset."""
    model = load_model(model_path, num_classes=num_classes, device=device)

    dataset = IsolineSegmentationDataset(
        images_dir=data_dir / "images",
        masks_dir=data_dir / "masks",
        input_size=input_size,
        augment=False,
    )
    loader = DataLoader(dataset, batch_size=batch_size, shuffle=False, num_workers=0)

    confusion = np.zeros((num_classes, num_classes), dtype=np.int64)
    all_ious: list[dict[int, float]] = []

    model.eval()
    with torch.no_grad():
        for batch in loader:
            images = batch["image"].to(device)
            masks = batch["mask"].numpy()

            logits = model(images)
            preds = logits.argmax(dim=1).cpu().numpy()

            for pred, target in zip(preds, masks, strict=False):
                ious = compute_iou(pred, target, num_classes)
                all_ious.append(ious)

                for t_class in range(num_classes):
                    for p_class in range(num_classes):
                        confusion[t_class, p_class] += np.sum(
                            (target == t_class) & (pred == p_class)
                        )

    # Aggregate metrics
    all_class_ious: dict[int, list[float]] = {}
    for sample_ious in all_ious:
        for cls, iou in sample_ious.items():
            all_class_ious.setdefault(cls, []).append(iou)

    per_class_iou = {cls: float(np.mean(vals)) for cls, vals in all_class_ious.items()}
    mean_iou = float(np.mean(list(per_class_iou.values()))) if per_class_iou else 0.0
    pixel_accuracy = float(np.diag(confusion).sum() / max(confusion.sum(), 1))

    return EvaluationResult(
        mean_iou=mean_iou,
        pixel_accuracy=pixel_accuracy,
        per_class_iou=per_class_iou,
        confusion_matrix=confusion,
        num_samples=len(dataset),
    )
