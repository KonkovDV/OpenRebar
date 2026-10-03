#!/usr/bin/env python3
"""Fail CI when dotnet package audit reports vulnerabilities at the configured severity."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

SEVERITY = {"low": 1, "moderate": 2, "high": 3, "critical": 4}


def findings(payload: dict[str, Any], threshold: str) -> list[tuple[str, str, str, str, str]]:
    minimum = SEVERITY[threshold]
    result: list[tuple[str, str, str, str, str]] = []
    for project in payload.get("projects", []):
        project_path = str(project.get("path", "unknown-project"))
        for framework in project.get("frameworks", []):
            framework_name = str(framework.get("framework", "unknown-framework"))
            packages = [
                *framework.get("topLevelPackages", []),
                *framework.get("transitivePackages", []),
            ]
            for package in packages:
                package_id = str(package.get("id", "unknown-package"))
                resolved = str(package.get("resolvedVersion", "unknown-version"))
                for vulnerability in package.get("vulnerabilities", []):
                    severity = str(vulnerability.get("severity", "unknown")).lower()
                    if SEVERITY.get(severity, 0) < minimum:
                        continue
                    advisory = str(vulnerability.get("advisoryurl", "unknown-advisory"))
                    result.append((severity, package_id, resolved, advisory, f"{project_path} ({framework_name})"))
    return sorted(result, key=lambda item: (-SEVERITY[item[0]], item[1], item[2], item[4]))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("report", type=Path)
    parser.add_argument("--threshold", choices=sorted(SEVERITY, key=SEVERITY.get), default="high")
    args = parser.parse_args()

    payload = json.loads(args.report.read_text(encoding="utf-8"))
    blocked = findings(payload, args.threshold)
    if not blocked:
        print(f"OK: no NuGet vulnerabilities at {args.threshold} severity or higher")
        return 0

    print(f"BLOCKED: {len(blocked)} NuGet vulnerabilities at {args.threshold} severity or higher")
    for severity, package, version, advisory, location in blocked:
        print(f"- {severity.upper()}: {package}@{version} {advisory} [{location}]")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
