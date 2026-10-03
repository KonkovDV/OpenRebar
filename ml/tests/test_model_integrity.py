from __future__ import annotations

import hashlib
import json
from pathlib import Path

import pytest

from src.security.model_integrity import (
    ManifestValidationError,
    load_manifest,
    verify_model_integrity,
)


def _manifest(path: Path, filename: str, digest: str) -> Path:
    path.write_text(
        json.dumps(
            {
                "schema_version": "1.0.0",
                "updated_at_utc": "2026-10-03T00:00:00Z",
                "repository": "KonkovDV/OpenRebar",
                "models": [
                    {
                        "model_id": "test-model",
                        "filename": filename,
                        "sha256": digest,
                    }
                ],
            }
        ),
        encoding="utf-8",
    )
    return path


def test_verify_model_integrity_accepts_registered_file(tmp_path: Path) -> None:
    model = tmp_path / "model.pt"
    model.write_bytes(b"trusted-model")
    digest = hashlib.sha256(model.read_bytes()).hexdigest()
    manifest = _manifest(tmp_path / "MANIFEST.json", model.name, digest)

    assert verify_model_integrity(model, manifest) == digest


def test_verify_model_integrity_rejects_modified_file(tmp_path: Path) -> None:
    model = tmp_path / "model.pt"
    model.write_bytes(b"modified")
    manifest = _manifest(tmp_path / "MANIFEST.json", model.name, "0" * 64)

    with pytest.raises(ManifestValidationError, match="SHA256 mismatch"):
        verify_model_integrity(model, manifest)


def test_manifest_rejects_path_traversal(tmp_path: Path) -> None:
    manifest = _manifest(tmp_path / "MANIFEST.json", "../model.pt", "0" * 64)

    with pytest.raises(ManifestValidationError, match="relative path"):
        load_manifest(manifest)


def test_explicit_digest_supports_external_read_only_volume(tmp_path: Path) -> None:
    model = tmp_path / "external.pt"
    model.write_bytes(b"external-model")
    digest = hashlib.sha256(model.read_bytes()).hexdigest()

    assert verify_model_integrity(
        model, tmp_path / "missing-manifest.json", expected_sha256=digest
    ) == digest
