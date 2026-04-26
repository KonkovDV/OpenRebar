#!/usr/bin/env python3
"""Generate IFC from canonical examples and emit a validation report.

This lane is designed for workflow_dispatch/nightly use to keep push CI fast.
"""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from dataclasses import asdict, dataclass
from pathlib import Path


@dataclass
class ExampleValidationResult:
    name: str
    input_path: str
    ifc_path: str
    exists: bool
    has_step_header: bool
    reinforcing_bar_count: int
    expected_min_reinforcing_bars: int
    status: str
    details: str


ROOT = Path(__file__).resolve().parents[2]
OUTPUT_DIR = ROOT / "artifacts" / "ifc-validation"
WORK_DIR = OUTPUT_DIR / "generated"
REPORT_PATH = OUTPUT_DIR / "ifc-validation-report.json"

EXAMPLES = [
    {
        "name": "dxf-simple-slab",
        "input": ROOT / "examples" / "dxf" / "simple-slab" / "input.dxf",
        "args": ["--thickness", "220", "--cover", "30", "--slab-width", "6000", "--slab-height", "4000"],
        "expected_min_reinforcing_bars": 1,
    },
    {
        "name": "png-simple-slab",
        "input": ROOT / "examples" / "png" / "simple-slab" / "input.png",
        "args": ["--thickness", "220", "--cover", "30", "--slab-width", "6000", "--slab-height", "4000"],
        "expected_min_reinforcing_bars": 0,
    },
]


def _run_cli(input_path: Path, extra_args: list[str]) -> subprocess.CompletedProcess[str]:
    command = [
        "dotnet",
        "run",
        "--project",
        "src/OpenRebar.Cli",
        "--configuration",
        "Release",
        "--",
        str(input_path),
        *extra_args,
    ]
    return subprocess.run(
        command,
        cwd=ROOT,
        text=True,
        capture_output=True,
        check=False,
    )


def _validate_ifc(example: dict[str, object]) -> ExampleValidationResult:
    name = str(example["name"])
    source_input = Path(example["input"])
    expected_min_bars = int(example["expected_min_reinforcing_bars"])
    extra_args = list(example["args"])

    destination_dir = WORK_DIR / name
    destination_dir.mkdir(parents=True, exist_ok=True)

    input_copy = destination_dir / source_input.name
    shutil.copy2(source_input, input_copy)

    process = _run_cli(input_copy, extra_args)
    if process.returncode != 0:
        return ExampleValidationResult(
            name=name,
            input_path=str(input_copy.relative_to(ROOT)),
            ifc_path=str(input_copy.with_suffix(".reinforcement.ifc").relative_to(ROOT)),
            exists=False,
            has_step_header=False,
            reinforcing_bar_count=0,
            expected_min_reinforcing_bars=expected_min_bars,
            status="failed",
            details=f"CLI exit code {process.returncode}: {process.stderr.strip() or process.stdout.strip()}",
        )

    ifc_path = input_copy.with_suffix(".reinforcement.ifc")
    exists = ifc_path.exists()
    if not exists:
        return ExampleValidationResult(
            name=name,
            input_path=str(input_copy.relative_to(ROOT)),
            ifc_path=str(ifc_path.relative_to(ROOT)),
            exists=False,
            has_step_header=False,
            reinforcing_bar_count=0,
            expected_min_reinforcing_bars=expected_min_bars,
            status="failed",
            details="IFC file was not produced.",
        )

    content = ifc_path.read_text(encoding="utf-8", errors="replace")
    upper_content = content.upper()

    has_step_header = upper_content.startswith("ISO-10303-21;")
    reinforcing_bar_count = upper_content.count("IFCREINFORCINGBAR(")

    is_ok = has_step_header and reinforcing_bar_count >= expected_min_bars

    details = "ok"
    if not has_step_header:
        details = "Missing STEP header in IFC content."
    elif reinforcing_bar_count < expected_min_bars:
        details = (
            f"Expected at least {expected_min_bars} IFCREINFORCINGBAR entries, "
            f"but found {reinforcing_bar_count}."
        )

    return ExampleValidationResult(
        name=name,
        input_path=str(input_copy.relative_to(ROOT)),
        ifc_path=str(ifc_path.relative_to(ROOT)),
        exists=exists,
        has_step_header=has_step_header,
        reinforcing_bar_count=reinforcing_bar_count,
        expected_min_reinforcing_bars=expected_min_bars,
        status="passed" if is_ok else "failed",
        details=details,
    )


def main() -> int:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    WORK_DIR.mkdir(parents=True, exist_ok=True)

    results = [_validate_ifc(example) for example in EXAMPLES]

    payload = {
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "results": [asdict(result) for result in results],
    }

    REPORT_PATH.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    failed = [result for result in results if result.status != "passed"]
    if failed:
        print("IFC validation failed:", file=sys.stderr)
        for item in failed:
            print(f"- {item.name}: {item.details}", file=sys.stderr)
        print(f"Report: {REPORT_PATH}", file=sys.stderr)
        return 1

    print(f"IFC validation passed. Report: {REPORT_PATH}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
