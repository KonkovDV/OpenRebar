#!/usr/bin/env python3
"""Validate the model manifest and optionally verify every listed artifact."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

ML_ROOT = Path(__file__).resolve().parents[1]
if str(ML_ROOT) not in sys.path:
    sys.path.insert(0, str(ML_ROOT))

from src.security.model_integrity import (  # noqa: E402
    ManifestValidationError,
    load_manifest,
    validate_manifest,
    verify_manifest_files,
)

__all__ = ["ManifestValidationError", "validate_manifest"]


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate OpenRebar ML model manifest")
    parser.add_argument(
        "manifest_path",
        nargs="?",
        default="ml/models/MANIFEST.json",
        help="Path to MANIFEST.json (default: ml/models/MANIFEST.json)",
    )
    parser.add_argument(
        "--verify-files",
        action="store_true",
        help="Hash every listed model file and fail when a file is absent or mismatched.",
    )
    args = parser.parse_args()
    manifest_path = Path(args.manifest_path)

    try:
        payload = load_manifest(manifest_path)
        if args.verify_files:
            verify_manifest_files(manifest_path)
    except ManifestValidationError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    count = len(payload["models"])
    suffix = " and model files" if args.verify_files else ""
    print(f"OK: Manifest{suffix} valid ({manifest_path}, {count} models)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
