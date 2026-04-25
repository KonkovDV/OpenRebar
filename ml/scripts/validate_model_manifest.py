#!/usr/bin/env python3
"""Validate ml/models/MANIFEST.json structure and sha256 formatting."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

SHA256_RE = re.compile(r"^[a-f0-9]{64}$")


class ManifestValidationError(ValueError):
    """Raised when model manifest content is invalid."""


def _expect_type(value: Any, expected_type: type[Any], field: str) -> None:
    if not isinstance(value, expected_type):
        raise ManifestValidationError(f"Field '{field}' must be {expected_type.__name__}")


def validate_manifest(payload: dict[str, Any]) -> None:
    _expect_type(payload, dict, "root")

    required_root_fields = ["schema_version", "updated_at_utc", "repository", "models"]
    for field in required_root_fields:
        if field not in payload:
            raise ManifestValidationError(f"Missing required field: '{field}'")

    _expect_type(payload["schema_version"], str, "schema_version")
    _expect_type(payload["updated_at_utc"], str, "updated_at_utc")
    _expect_type(payload["repository"], str, "repository")
    _expect_type(payload["models"], list, "models")

    for idx, model in enumerate(payload["models"]):
        prefix = f"models[{idx}]"
        _expect_type(model, dict, prefix)

        for field in ["model_id", "filename", "sha256"]:
            if field not in model:
                raise ManifestValidationError(f"Missing required field: '{prefix}.{field}'")
            _expect_type(model[field], str, f"{prefix}.{field}")

        if not SHA256_RE.match(model["sha256"]):
            raise ManifestValidationError(
                f"Field '{prefix}.sha256' must be lowercase 64-char SHA256 hex"
            )


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate OpenRebar ML model manifest")
    parser.add_argument(
        "manifest_path",
        nargs="?",
        default="ml/models/MANIFEST.json",
        help="Path to MANIFEST.json (default: ml/models/MANIFEST.json)",
    )
    args = parser.parse_args()

    manifest_path = Path(args.manifest_path)
    if not manifest_path.exists():
        print(f"ERROR: Manifest file not found: {manifest_path}", file=sys.stderr)
        return 1

    try:
        payload = json.loads(manifest_path.read_text(encoding="utf-8"))
        validate_manifest(payload)
    except (json.JSONDecodeError, ManifestValidationError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    print(f"OK: Manifest is valid ({manifest_path})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
