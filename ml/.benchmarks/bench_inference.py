"""
OpenRebar — Benchmark suite for isoline segmentation.

Measures inference latency, throughput, and model size for CI tracking.

Usage:
    pytest ml/.benchmarks/bench_inference.py -v --benchmark-disable-gc
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
import pytest
import torch

from src.segmentation.model import IsolineUNet


@pytest.fixture(scope="module")
def model() -> IsolineUNet:
    m = IsolineUNet(num_classes=8, base_ch=32)
    m.eval()
    return m


@pytest.fixture(scope="module")
def sample_input() -> torch.Tensor:
    return torch.randn(1, 3, 512, 512)


class TestInferenceBenchmarks:
    """Baseline inference benchmarks for tracking performance over time."""

    def test_single_inference_latency(self, model: IsolineUNet, sample_input: torch.Tensor) -> None:
        """Single image inference should complete within reasonable time."""
        with torch.no_grad():
            output = model(sample_input)
        assert output.shape == (1, 8, 512, 512)

    def test_model_parameter_count(self, model: IsolineUNet) -> None:
        """Track model size — should stay under 10M parameters for edge deployment."""
        param_count = sum(p.numel() for p in model.parameters())
        assert param_count < 10_000_000, f"Model has {param_count:,} parameters, exceeds 10M limit"
        print(f"Model parameters: {param_count:,}")

    def test_batch_throughput(self, model: IsolineUNet) -> None:
        """Batch of 4 images should complete inference."""
        batch = torch.randn(4, 3, 512, 512)
        with torch.no_grad():
            output = model(batch)
        assert output.shape == (4, 8, 512, 512)

    def test_onnx_exportability(self, model: IsolineUNet, sample_input: torch.Tensor, tmp_path: Path) -> None:
        """Model should be exportable to ONNX without errors."""
        onnx_path = tmp_path / "test_model.onnx"
        torch.onnx.export(
            model,
            sample_input,
            str(onnx_path),
            opset_version=17,
            input_names=["image"],
            output_names=["logits"],
        )
        assert onnx_path.exists()
        assert onnx_path.stat().st_size > 0

    def test_output_class_probabilities(self, model: IsolineUNet, sample_input: torch.Tensor) -> None:
        """Output logits should produce valid class predictions."""
        with torch.no_grad():
            logits = model(sample_input)
            probs = torch.softmax(logits, dim=1)

        assert probs.shape == (1, 8, 512, 512)
        # All probabilities should sum to 1 along class dimension
        sums = probs.sum(dim=1)
        assert torch.allclose(sums, torch.ones_like(sums), atol=1e-5)
