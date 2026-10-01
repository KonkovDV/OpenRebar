from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

from src.training.dataset import IsolineSegmentationDataset
from src.training.evaluate import evaluate
from src.training.export_onnx import export_to_onnx
from src.training.train import train


def _write_sample(image_path: Path, mask_path: Path, class_id: int) -> None:
    image = np.zeros((32, 32, 3), dtype=np.uint8)
    image[4:28, 4:28, 0] = 80 + class_id * 20
    image[8:24, 8:24, 1] = 120
    image[12:20, 12:20, 2] = 200 - class_id * 10

    mask = np.zeros((32, 32), dtype=np.uint8)
    mask[6:26, 6:26] = class_id

    Image.fromarray(image).save(image_path)
    Image.fromarray(mask, mode="L").save(mask_path)


def _create_dataset_split(root: Path, split: str, class_ids: list[int]) -> Path:
    split_dir = root / split
    images_dir = split_dir / "images"
    masks_dir = split_dir / "masks"
    images_dir.mkdir(parents=True, exist_ok=True)
    masks_dir.mkdir(parents=True, exist_ok=True)

    for index, class_id in enumerate(class_ids):
        filename = f"sample_{index}.png"
        _write_sample(images_dir / filename, masks_dir / filename, class_id)

    return split_dir


def test_dataset_loads_paired_training_samples(tmp_path: Path) -> None:
    split_dir = _create_dataset_split(tmp_path, "train", [1, 2])

    dataset = IsolineSegmentationDataset(
        images_dir=split_dir / "images",
        masks_dir=split_dir / "masks",
        input_size=(32, 32),
        augment=False,
    )

    sample = dataset[0]

    assert len(dataset) == 2
    assert tuple(sample["image"].shape) == (3, 32, 32)
    assert tuple(sample["mask"].shape) == (32, 32)
    assert sample["path"].endswith("sample_0.png")


def test_dataset_raises_when_mask_is_missing(tmp_path: Path) -> None:
    split_dir = tmp_path / "train"
    images_dir = split_dir / "images"
    masks_dir = split_dir / "masks"
    images_dir.mkdir(parents=True, exist_ok=True)
    masks_dir.mkdir(parents=True, exist_ok=True)

    _write_sample(images_dir / "orphan.png", masks_dir / "other.png", class_id=1)

    dataset = IsolineSegmentationDataset(
        images_dir=images_dir,
        masks_dir=masks_dir,
        input_size=(32, 32),
        augment=False,
    )

    try:
        dataset[0]
    except FileNotFoundError as exc:
        assert "Cannot read mask" in str(exc)
    else:
        raise AssertionError("Expected missing-mask access to fail")


def test_training_evaluation_and_onnx_export_smoke(tmp_path: Path) -> None:
    train_dir = _create_dataset_split(tmp_path, "train", [1, 2])
    val_dir = _create_dataset_split(tmp_path, "val", [1])
    checkpoint_path = tmp_path / "models" / "tiny_unet.pt"
    onnx_path = tmp_path / "models" / "tiny_unet.onnx"

    output_path = train(
        data_dir=train_dir,
        val_dir=val_dir,
        epochs=1,
        batch_size=1,
        lr=1e-3,
        num_classes=3,
        input_size=(32, 32),
        output_path=checkpoint_path,
        device="cpu",
    )

    evaluation = evaluate(
        model_path=output_path,
        data_dir=val_dir,
        num_classes=3,
        input_size=(32, 32),
        batch_size=1,
        device="cpu",
    )

    export_result = export_to_onnx(
        model_path=output_path,
        output_path=onnx_path,
        num_classes=3,
        input_size=(32, 32),
    )

    assert output_path.exists()
    assert output_path.stat().st_size > 0
    assert evaluation.num_samples == 1
    assert evaluation.confusion_matrix.shape == (3, 3)
    assert 0.0 <= evaluation.pixel_accuracy <= 1.0
    assert 0.0 <= evaluation.mean_iou <= 1.0
    assert export_result.exists()
    assert export_result.stat().st_size > 0