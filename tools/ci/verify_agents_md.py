#!/usr/bin/env python3
"""Check AGENTS.md against the commands actually run in ci.yml."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CI = ROOT / ".github" / "workflows" / "ci.yml"
AGENTS = ROOT / "AGENTS.md"

COMMAND = re.compile(r"^(?:dotnet|pytest|python3?)(?:\s|$)")
ENV_DOTNET = re.compile(r'^OPENREBAR_\S+="[^"]*"\s+dotnet\s')


def ci_commands(text: str) -> list[str]:
    found: list[str] = []
    seen: set[str] = set()
    for raw in text.splitlines():
        line = raw.strip()
        if line.startswith("run:"):
            line = line[4:].strip()
        if line.startswith("python - <<"):
            continue
        if not (COMMAND.match(line) or ENV_DOTNET.match(line)):
            continue
        if line in seen:
            continue
        seen.add(line)
        found.append(line)
    return found


def main() -> int:
    errors: list[str] = []
    agents = AGENTS.read_text(encoding="utf-8")
    for command in ci_commands(CI.read_text(encoding="utf-8")):
        if command not in agents:
            errors.append(f"AGENTS.md is missing a CI command: {command}")

    required = [
        ROOT / "CLAUDE.md",
        ROOT / "src" / "OpenRebar.Domain" / "AGENTS.md",
        ROOT / "src" / "OpenRebar.RevitPlugin" / "AGENTS.md",
        ROOT / "ml" / "AGENTS.md",
        ROOT / "docs" / "agent" / "specs",
        ROOT / "docs" / "agent" / "plans",
        ROOT / "docs" / "agent" / "decisions" / "DECISIONS.md",
        ROOT / "docs" / "agent" / "measurements",
    ]
    for path in required:
        if not path.exists():
            errors.append(f"missing {path.relative_to(ROOT)}")

    claude = (ROOT / "CLAUDE.md").read_text(encoding="utf-8").strip().splitlines()
    if len(claude) != 1 or "AGENTS.md" not in claude[0]:
        errors.append("CLAUDE.md must be one line that points at AGENTS.md")

    domain_csproj = (ROOT / "src" / "OpenRebar.Domain" / "OpenRebar.Domain.csproj").read_text(
        encoding="utf-8"
    )
    if "PackageReference" in domain_csproj:
        errors.append("OpenRebar.Domain.csproj must not take a NuGet package")

    domain_agents = (ROOT / "src" / "OpenRebar.Domain" / "AGENTS.md").read_text(encoding="utf-8")
    if "NuGet" not in domain_agents and "PackageReference" not in domain_agents:
        errors.append("Domain AGENTS.md must state that the project has no NuGet packages")

    revit = (ROOT / "src" / "OpenRebar.RevitPlugin" / "AGENTS.md").read_text(encoding="utf-8")
    for token in ("REVIT_SDK", "RevitNet8", "RevitNet10"):
        if token not in revit:
            errors.append(f"Revit AGENTS.md must mention {token}")

    ml = (ROOT / "ml" / "AGENTS.md").read_text(encoding="utf-8")
    if "requirements.locked.txt" not in ml or "offline" not in ml.lower():
        errors.append("ml/AGENTS.md must keep the lockfile and the offline rule")

    if errors:
        print("AGENTS.md verification failed:", file=sys.stderr)
        for issue in errors:
            print(f"- {issue}", file=sys.stderr)
        return 1

    print(f"AGENTS.md matches {len(ci_commands(CI.read_text(encoding='utf-8')))} CI commands")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
