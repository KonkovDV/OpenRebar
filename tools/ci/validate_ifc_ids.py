#!/usr/bin/env python3
"""Check generated IFC files against the entity names required by an IDS file.

IfcOpenShell is LGPL-3.0. This check does not add that package. It reads the
IDS 1.0 simpleValue names that start with IFC and requires each one in the
STEP file. The official IDS schema can still be applied later with a
separately licensed tool.
"""

from __future__ import annotations

import sys
import xml.etree.ElementTree as xml
from pathlib import Path


def required_entities(ids_path: Path) -> list[str]:
    root = xml.parse(ids_path).getroot()
    names: list[str] = []
    for node in root.iter():
        if node.tag.endswith("simpleValue") and node.text:
            value = node.text.strip().upper()
            if value.startswith("IFC") and value not in names:
                names.append(value)
    if not names:
        raise SystemExit(f"{ids_path} does not name an IFC entity")
    return names


def check_file(ifc_path: Path, entities: list[str]) -> list[str]:
    text = ifc_path.read_text(encoding="utf-8", errors="replace").upper()
    missing = [name for name in entities if name + "(" not in text]
    if "METRE" not in text:
        missing.append("METRE")
    return missing


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: validate_ifc_ids.py <ifc-dir> <ids-file>", file=sys.stderr)
        return 2

    ifc_dir = Path(sys.argv[1])
    entities = required_entities(Path(sys.argv[2]))
    files = sorted(ifc_dir.rglob("*.ifc"))
    if not files:
        print(f"No IFC files in {ifc_dir}", file=sys.stderr)
        return 1

    failed = False
    for ifc_path in files:
        missing = check_file(ifc_path, entities)
        if missing:
            failed = True
            print(f"{ifc_path}: missing {', '.join(missing)}", file=sys.stderr)
        else:
            print(f"{ifc_path}: ids entities present")

    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
