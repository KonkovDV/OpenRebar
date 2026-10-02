#!/usr/bin/env python3
"""Compare SHA-256 files written by the same check on net8.0 and net10.0."""

from __future__ import annotations

import sys
from pathlib import Path


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: compare_tfm_hashes.py <net8-dir> <net10-dir>", file=sys.stderr)
        return 2

    left = Path(sys.argv[1])
    right = Path(sys.argv[2])
    names = sorted(path.name for path in left.glob("*.sha256"))
    if not names:
        print(f"no .sha256 files in {left}", file=sys.stderr)
        return 1

    errors: list[str] = []
    for name in names:
        first = (left / name).read_text(encoding="utf-8").strip()
        other = right / name
        if not other.exists():
            errors.append(f"missing {other}")
            continue
        second = other.read_text(encoding="utf-8").strip()
        if first != second:
            errors.append(f"{name}: net8 {first} != net10 {second}")
        else:
            print(f"match {name} {first}")

    if errors:
        for issue in errors:
            print(issue, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
