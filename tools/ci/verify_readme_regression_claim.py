#!/usr/bin/env python3
"""Verify README regression test-count claims against TRX totals.

The CI test job writes TRX files via:
  dotnet test ... --logger "trx;LogFileName=test-results.trx"

This script aggregates all matching TRX counters and validates:
- README.md contains **N/N tests passing**
- README.ru.md contains **N/N тестов проходят**
- EN and RU claims match each other and the TRX total
"""

from __future__ import annotations

import re
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
README_EN = ROOT / "README.md"
README_RU = ROOT / "README.ru.md"

EN_PATTERN = re.compile(r"\*\*(\d+)/(\d+) tests passing\*\*")
RU_PATTERN = re.compile(r"\*\*(\d+)/(\d+) тестов проходят\*\*")


def _local_name(tag: str) -> str:
    if "}" in tag:
        return tag.rsplit("}", 1)[1]
    return tag


def _find_first_by_local_name(root: ET.Element, name: str) -> ET.Element | None:
    for element in root.iter():
        if _local_name(element.tag) == name:
            return element
    return None


def aggregate_trx_counters(root: Path) -> tuple[int, int, int]:
    trx_files = sorted(root.rglob("test-results.trx"))
    if not trx_files:
        raise RuntimeError("No TRX files found (expected one or more test-results.trx files).")

    total = 0
    passed = 0
    failed = 0

    for trx in trx_files:
        tree = ET.parse(trx)
        counters = _find_first_by_local_name(tree.getroot(), "Counters")
        if counters is None:
            raise RuntimeError(f"Missing <Counters> in TRX file: {trx}")

        total += int(counters.attrib.get("total", "0"))
        passed += int(counters.attrib.get("passed", "0"))
        failed += int(counters.attrib.get("failed", "0"))

    return total, passed, failed


def parse_claim(path: Path, pattern: re.Pattern[str], label: str) -> tuple[int, int]:
    text = path.read_text(encoding="utf-8")
    match = pattern.search(text)
    if match is None:
        raise RuntimeError(f"Could not find regression status claim in {label}: {path}")

    claimed_passed = int(match.group(1))
    claimed_total = int(match.group(2))
    return claimed_passed, claimed_total


def main() -> int:
    total, passed, failed = aggregate_trx_counters(ROOT)

    en_passed, en_total = parse_claim(README_EN, EN_PATTERN, "README.md")
    ru_passed, ru_total = parse_claim(README_RU, RU_PATTERN, "README.ru.md")

    errors: list[str] = []

    if failed != 0:
        errors.append(
            f"TRX reports failed tests (failed={failed}); README regression claim requires green suite."
        )

    if (en_passed, en_total) != (ru_passed, ru_total):
        errors.append(
            "README EN/RU regression claims diverge: "
            f"README.md={en_passed}/{en_total}, README.ru.md={ru_passed}/{ru_total}."
        )

    if (en_passed, en_total) != (passed, total):
        errors.append(
            "README regression claim does not match TRX aggregate: "
            f"claimed={en_passed}/{en_total}, actual={passed}/{total}."
        )

    if errors:
        print("README regression claim verification failed:", file=sys.stderr)
        for issue in errors:
            print(f"- {issue}", file=sys.stderr)
        return 1

    print(
        "README regression claim verified: "
        f"{passed}/{total} (failed={failed}) matches README.md and README.ru.md"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
