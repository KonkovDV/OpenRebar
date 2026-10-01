"""
OpenRebar — Export trained U-Net to ONNX for CPU-only C# inference.

Usage:
    python -m src.training.export_onnx --model models/isoline_unet.pt --output models/isoline_unet.onnx
"""

from __future__ import annotations

import argparse
import inspect
from pathlib import Path
from typing import Any, Callable, cast

import torch

from ..segmentation.model import IsolineUNet


def export_to_onnx(
    model_path: Path,
    output_path: Path,
    num_classes: int = 8,
    input_size: tuple[int, int] = (512, 512),
    opset_version: int = 18,
) -> Path:
    """Export a trained IsolineUNet checkpoint to ONNX format."""
    model = IsolineUNet(num_classes=num_classes)
    state = torch.load(model_path, map_location="cpu", weights_only=True)
    model.load_state_dict(state)
    model.eval()

    dummy_input = torch.randn(1, 3, *input_size)

    output_path.parent.mkdir(parents=True, exist_ok=True)

    export_fn = cast(Callable[..., object], getattr(torch.onnx, "export"))
    export_signature = inspect.signature(export_fn)
    supports_dynamic_shapes = "dynamic_shapes" in export_signature.parameters
    supports_dynamo = "dynamo" in export_signature.parameters

    if supports_dynamic_shapes:
        onnx_program = export_fn(
            model,
            (dummy_input,),
            opset_version=opset_version,
            input_names=["image"],
            output_names=["logits"],
            dynamic_shapes=({0: "batch", 2: "height", 3: "width"},),
            dynamo=True if supports_dynamo else False,
        )

        if onnx_program is None or not hasattr(onnx_program, "save"):
            raise RuntimeError("torch.onnx.export did not return a serializable ONNXProgram")

        cast(Any, onnx_program).save(output_path)
    else:
        export_fn(
            model,
            (dummy_input,),
            str(output_path),
            opset_version=opset_version,
            input_names=["image"],
            output_names=["logits"],
            dynamic_axes={
                "image": {0: "batch", 2: "height", 3: "width"},
                "logits": {0: "batch", 2: "height", 3: "width"},
            },
        )

    print(f"Exported ONNX model to {output_path}")
    return output_path


def main() -> None:
    parser = argparse.ArgumentParser(description="Export OpenRebar U-Net to ONNX")
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path("models/isoline_unet.onnx"))
    parser.add_argument("--num-classes", type=int, default=8)
    parser.add_argument("--opset", type=int, default=18)
    args = parser.parse_args()

    export_to_onnx(args.model, args.output, args.num_classes, opset_version=args.opset)


if __name__ == "__main__":
    main()
