"""Fail-closed integrity checks for model artifacts and their manifest."""

from __future__ import annotations

import hashlib
import hmac
import json
import re
from pathlib import Path
from typing import Any

SHA256_RE = re.compile(r"^[a-f0-9]{64}$")


class ManifestValidationError(ValueError):
    """Raised when model provenance or integrity evidence is invalid."""


def _expect_type(value: Any, expected_type: type[Any], field: str) -> None:
    if not isinstance(value, expected_type):
        raise ManifestValidationError(
            f"Field '{field}' must be {expected_type.__name__}"
        )


def _safe_relative_filename(value: str, field: str) -> Path:
    filename = Path(value)
    if not value or filename.is_absolute() or ".." in filename.parts:
        raise ManifestValidationError(
            f"Field '{field}' must be a non-empty relative path without '..'"
        )
    return filename


def validate_manifest(payload: dict[str, Any]) -> None:
    """Validate manifest shape, uniqueness and path safety."""
    _expect_type(payload, dict, "root")
    for field in ["schema_version", "updated_at_utc", "repository", "models"]:
        if field not in payload:
            raise ManifestValidationError(f"Missing required field: '{field}'")

    _expect_type(payload["schema_version"], str, "schema_version")
    _expect_type(payload["updated_at_utc"], str, "updated_at_utc")
    _expect_type(payload["repository"], str, "repository")
    _expect_type(payload["models"], list, "models")

    model_ids: set[str] = set()
    filenames: set[str] = set()
    for idx, model in enumerate(payload["models"]):
        prefix = f"models[{idx}]"
        _expect_type(model, dict, prefix)
        for field in ["model_id", "filename", "sha256"]:
            if field not in model:
                raise ManifestValidationError(
                    f"Missing required field: '{prefix}.{field}'"
                )
            _expect_type(model[field], str, f"{prefix}.{field}")

        model_id = model["model_id"]
        if not model_id or model_id in model_ids:
            raise ManifestValidationError(
                f"Field '{prefix}.model_id' must be non-empty and unique"
            )
        model_ids.add(model_id)

        filename = _safe_relative_filename(
            model["filename"], f"{prefix}.filename"
        ).as_posix()
        if filename in filenames:
            raise ManifestValidationError(
                f"Field '{prefix}.filename' must be unique"
            )
        filenames.add(filename)

        if not SHA256_RE.fullmatch(model["sha256"]):
            raise ManifestValidationError(
                f"Field '{prefix}.sha256' must be lowercase 64-char SHA256 hex"
            )


def load_manifest(manifest_path: Path) -> dict[str, Any]:
    """Load and validate a JSON model manifest."""
    try:
        payload = json.loads(manifest_path.read_text(encoding="utf-8"))
    except OSError as exc:
        raise ManifestValidationError(
            f"Cannot read model manifest: {manifest_path}"
        ) from exc
    except json.JSONDecodeError as exc:
        raise ManifestValidationError(
            f"Invalid JSON in model manifest: {manifest_path}"
        ) from exc
    validate_manifest(payload)
    return payload


def sha256_file(path: Path) -> str:
    """Hash a file without loading it into memory."""
    digest = hashlib.sha256()
    try:
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
    except OSError as exc:
        raise ManifestValidationError(f"Cannot read model file: {path}") from exc
    return digest.hexdigest()


def verify_model_integrity(
    model_path: Path,
    manifest_path: Path,
    expected_sha256: str | None = None,
) -> str:
    """Verify the selected model against an explicit digest or its manifest entry."""
    model_path = model_path.resolve()
    if not model_path.is_file():
        raise ManifestValidationError(f"Model file not found: {model_path}")

    if expected_sha256 is not None:
        if not SHA256_RE.fullmatch(expected_sha256):
            raise ManifestValidationError(
                "OpenRebar_MODEL_SHA256 must be lowercase 64-char SHA256 hex"
            )
        expected = expected_sha256
    else:
        payload = load_manifest(manifest_path)
        root = manifest_path.resolve().parent
        match: dict[str, Any] | None = None
        for entry in payload["models"]:
            candidate = (root / _safe_relative_filename(
                entry["filename"], "model.filename"
            )).resolve()
            if not candidate.is_relative_to(root):
                raise ManifestValidationError(
                    f"Model path escapes manifest directory: {entry['filename']}"
                )
            if candidate == model_path:
                match = entry
                break
        if match is None:
            raise ManifestValidationError(
                f"Model is not registered in manifest: {model_path}"
            )
        expected = match["sha256"]

    actual = sha256_file(model_path)
    if not hmac.compare_digest(actual, expected):
        raise ManifestValidationError(
            f"Model SHA256 mismatch for {model_path.name}: expected {expected}, got {actual}"
        )
    return actual


def verify_manifest_files(manifest_path: Path) -> list[str]:
    """Verify every manifest entry; intended for downloaded release bundles."""
    payload = load_manifest(manifest_path)
    root = manifest_path.resolve().parent
    return [
        verify_model_integrity(root / entry["filename"], manifest_path)
        for entry in payload["models"]
    ]
