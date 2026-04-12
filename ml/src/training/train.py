"""
OpenRebar — U-Net training script.

Usage:
    python -m src.training.train --data-dir data/train --val-dir data/val --epochs 50
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import torch
import torch.nn as nn
from torch.utils.data import DataLoader

from ..segmentation.model import IsolineUNet
from .dataset import IsolineSegmentationDataset


def train(
    data_dir: Path,
    val_dir: Path | None,
    epochs: int = 50,
    batch_size: int = 4,
    lr: float = 1e-3,
    num_classes: int = 8,
    input_size: tuple[int, int] = (512, 512),
    output_path: Path = Path("models/isoline_unet.pt"),
    device: str = "cpu",
) -> Path:
    """Train the IsolineUNet model and save the best checkpoint."""
    model = IsolineUNet(num_classes=num_classes).to(device)
    optimizer = torch.optim.AdamW(model.parameters(), lr=lr, weight_decay=1e-4)
    scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(optimizer, T_max=epochs)
    criterion = nn.CrossEntropyLoss()

    train_ds = IsolineSegmentationDataset(
        images_dir=data_dir / "images",
        masks_dir=data_dir / "masks",
        input_size=input_size,
        augment=True,
    )
    train_loader = DataLoader(
        train_ds, batch_size=batch_size, shuffle=True, num_workers=0, pin_memory=True
    )

    val_loader = None
    if val_dir and (val_dir / "images").exists():
        val_ds = IsolineSegmentationDataset(
            images_dir=val_dir / "images",
            masks_dir=val_dir / "masks",
            input_size=input_size,
            augment=False,
        )
        val_loader = DataLoader(val_ds, batch_size=batch_size, shuffle=False, num_workers=0)

    best_val_loss = float("inf")
    output_path.parent.mkdir(parents=True, exist_ok=True)

    for epoch in range(1, epochs + 1):
        # Training
        model.train()
        train_loss = 0.0
        for batch in train_loader:
            images = batch["image"].to(device)
            masks = batch["mask"].to(device)

            optimizer.zero_grad()
            logits = model(images)
            loss = criterion(logits, masks)
            loss.backward()
            optimizer.step()
            train_loss += loss.item()

        scheduler.step()
        avg_train = train_loss / len(train_loader)

        # Validation
        avg_val = float("inf")
        if val_loader:
            model.eval()
            val_loss = 0.0
            with torch.no_grad():
                for batch in val_loader:
                    images = batch["image"].to(device)
                    masks = batch["mask"].to(device)
                    logits = model(images)
                    val_loss += criterion(logits, masks).item()
            avg_val = val_loss / len(val_loader)

        print(f"Epoch {epoch:3d}/{epochs} | train_loss={avg_train:.4f} | val_loss={avg_val:.4f}")

        # Save best
        if avg_val < best_val_loss:
            best_val_loss = avg_val
            torch.save(model.state_dict(), output_path)
            print(f"  → Saved best model (val_loss={avg_val:.4f})")

    # If no validation, save final model
    if val_loader is None:
        torch.save(model.state_dict(), output_path)
        print(f"  → Saved final model to {output_path}")

    return output_path


def main() -> None:
    parser = argparse.ArgumentParser(description="Train OpenRebar isoline segmentation model")
    parser.add_argument("--data-dir", type=Path, required=True, help="Training data directory")
    parser.add_argument("--val-dir", type=Path, default=None, help="Validation data directory")
    parser.add_argument("--epochs", type=int, default=50)
    parser.add_argument("--batch-size", type=int, default=4)
    parser.add_argument("--lr", type=float, default=1e-3)
    parser.add_argument("--num-classes", type=int, default=8)
    parser.add_argument("--output", type=Path, default=Path("models/isoline_unet.pt"))
    parser.add_argument("--device", type=str, default="cuda" if torch.cuda.is_available() else "cpu")
    args = parser.parse_args()

    train(
        data_dir=args.data_dir,
        val_dir=args.val_dir,
        epochs=args.epochs,
        batch_size=args.batch_size,
        lr=args.lr,
        num_classes=args.num_classes,
        output_path=args.output,
        device=args.device,
    )


if __name__ == "__main__":
    main()
