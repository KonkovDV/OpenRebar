"""Draw the plan of a run from the files the CLI wrote.

  python tools/examples/render_plan.py <folder>/input out.svg

reads input.reinforcement.ifc, input.verification.json, and input.result.json.
Bars are the swept-disk directrices projected onto the slab plane. Cells are
the under-reinforced cells, merged into rows. A ring marks each hard clash.
"""

from __future__ import annotations

import json
import math
import re
import sys
from pathlib import Path

NAVY = "#142235"
ORANGE = "#FF6738"
STONE = "#F5F4F0"
EDGE_TINT = "#FFC7AE"

ENTITY = re.compile(r"^(#\d+)\s*=\s*([A-Z0-9_]+)\((.*)\);\s*$", re.M)


def parse_args(text: str):
    """Parse a STEP argument list into nested Python lists."""
    pos = 0

    def value():
        nonlocal pos
        while text[pos] == " ":
            pos += 1
        ch = text[pos]
        if ch == "(":
            pos += 1
            items = []
            while True:
                while text[pos] == " ":
                    pos += 1
                if text[pos] == ")":
                    pos += 1
                    return items
                items.append(value())
                while text[pos] == " ":
                    pos += 1
                if text[pos] == ",":
                    pos += 1
        if ch == "'":
            end = pos + 1
            while True:
                if text[end] == "'" and (end + 1 >= len(text) or text[end + 1] != "'"):
                    break
                end += 2 if text[end] == "'" else 1
            token = text[pos + 1:end]
            pos = end + 1
            return token
        match = re.compile(r"[^,()]+").match(text, pos)
        token = match.group(0).strip()
        pos = match.end()
        if token.startswith("#"):
            return token
        if token in ("$", "*") or token.startswith("."):
            return token
        if token.startswith("IFC"):
            inner = value()
            return (token, inner)
        return float(token)

    items = []
    while pos < len(text):
        items.append(value())
        while pos < len(text) and text[pos] in " ,":
            pos += 1
    return items


def load_ifc(path: Path):
    text = path.read_text(encoding="utf-8", errors="replace")
    return {key: (kind, parse_args(args)) for key, kind, args in ENTITY.findall(text)}


def point(ents, ref):
    kind, args = ents[ref]
    coords = list(args[0]) + [0.0] * (3 - len(args[0]))
    return coords[:3]


def direction(ents, ref, default):
    if ref == "$":
        return default
    kind, args = ents[ref]
    values = list(args[0]) + [0.0] * (3 - len(args[0]))
    norm = math.sqrt(sum(v * v for v in values)) or 1.0
    return [v / norm for v in values]


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def axis_placement(ents, ref):
    kind, args = ents[ref]
    origin = point(ents, args[0])
    z = direction(ents, args[1] if len(args) > 1 else "$", [0.0, 0.0, 1.0])
    x = direction(ents, args[2] if len(args) > 2 else "$", [1.0, 0.0, 0.0])
    dot = sum(a * b for a, b in zip(x, z))
    x = [a - dot * b for a, b in zip(x, z)]
    norm = math.sqrt(sum(v * v for v in x)) or 1.0
    x = [v / norm for v in x]
    y = cross(z, x)
    return origin, (x, y, z)


def compose(outer, inner):
    (o1, (x1, y1, z1)), (o2, (x2, y2, z2)) = outer, inner

    def apply(v):
        return [x1[i] * v[0] + y1[i] * v[1] + z1[i] * v[2] for i in range(3)]

    origin = [o1[i] + apply(o2)[i] for i in range(3)]
    return origin, (apply(x2), apply(y2), apply(z2))


def local_placement(ents, ref):
    kind, args = ents[ref]
    here = axis_placement(ents, args[1])
    if args[0] == "$":
        return here
    return compose(local_placement(ents, args[0]), here)


def curve_points(ents, ref):
    kind, args = ents[ref]
    if kind == "IFCPOLYLINE":
        return [point(ents, p) for p in args[0]]
    if kind == "IFCCOMPOSITECURVE":
        pts = []
        for segment in args[0]:
            seg_kind, seg_args = ents[segment]
            part = curve_points(ents, seg_args[2])
            if seg_args[1] == ".F.":
                part = part[::-1]
            if pts and part and part[0] == pts[-1]:
                part = part[1:]
            pts.extend(part)
        return pts
    raise ValueError(f"unsupported directrix {kind}")


def bar_paths(ents):
    paths = []
    for key, (kind, args) in ents.items():
        if kind != "IFCREINFORCINGBAR":
            continue
        origin, (x, y, z) = local_placement(ents, args[5])
        diameter = args[9] * 1000.0
        _, shape = ents[args[6]]
        for rep in shape[2]:
            _, rep_args = ents[rep]
            for item in rep_args[3]:
                item_kind, item_args = ents[item]
                if item_kind != "IFCSWEPTDISKSOLID":
                    continue
                world = []
                for p in curve_points(ents, item_args[0]):
                    w = [origin[i] + x[i] * p[0] + y[i] * p[1] + z[i] * p[2] for i in range(3)]
                    world.append((w[0] * 1000.0, w[1] * 1000.0))
                paths.append((diameter, world))
    return paths


def cell_rows(cells, size):
    rows = {}
    for cell in cells:
        rows.setdefault((cell["y"], cell["status"]), []).append(cell["x"])
    rects = []
    for (y, status), xs in sorted(rows.items()):
        xs.sort()
        start = prev = xs[0]
        for x in xs[1:] + [None]:
            if x is not None and abs(x - prev - size) < 1e-6:
                prev = x
                continue
            rects.append((status, start - size / 2, y - size / 2, prev - start + size, size))
            if x is not None:
                start = prev = x
    return rects


def render(run: Path, out_path: Path) -> None:
    ents = load_ifc(run.with_name(run.name + ".reinforcement.ifc"))
    paths = bar_paths(ents)
    verification = json.loads(run.with_name(run.name + ".verification.json").read_text(encoding="utf-8"))
    report = json.loads(run.with_name(run.name + ".result.json").read_text(encoding="utf-8"))
    size = verification["cellSizeMm"]
    rects = cell_rows(verification["cells"], size)

    box = report["slab"]["boundingBox"]
    width, height = float(box["width"]), float(box["height"])
    hard = [c for c in report["clashes"] if c["type"] == "hard"]

    pad = 320.0
    view_w, view_h = width + 2 * pad, height + 2 * pad

    def sx(x):
        return round(pad + x, 1)

    def sy(y):
        return round(pad + height - y, 1)

    lines = [
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {view_w:.0f} {view_h:.0f}" '
        f'width="{view_w / 7.5:.0f}" height="{view_h / 7.5:.0f}" role="img" '
        f'aria-label="Plan of the house slab: bars and under-reinforced cells">',
        f'<rect width="{view_w:.0f}" height="{view_h:.0f}" rx="60" fill="{STONE}"/>',
        f'<rect x="{sx(0)}" y="{sy(height)}" width="{width:.0f}" height="{height:.0f}" fill="#FFFFFF"/>',
    ]
    for status, x, y, w, h in rects:
        fill = ORANGE if status == "RealDeficit" else EDGE_TINT
        lines.append(f'<rect x="{sx(x)}" y="{sy(y + h)}" width="{w:.0f}" height="{h:.0f}" fill="{fill}"/>')
    for diameter, path in paths:
        coords = " ".join(f"{sx(px)},{sy(py)}" for px, py in path)
        lines.append(
            f'<polyline points="{coords}" fill="none" stroke="{NAVY}" '
            f'stroke-width="{diameter:.0f}" stroke-linecap="round" stroke-linejoin="round"/>'
        )
    lines.append(
        f'<rect x="{sx(0)}" y="{sy(height)}" width="{width:.0f}" height="{height:.0f}" '
        f'fill="none" stroke="{NAVY}" stroke-width="12"/>'
    )
    for clash in hard:
        lines.append(
            f'<circle cx="{sx(clash["x"])}" cy="{sy(clash["y"])}" r="130" fill="none" '
            f'stroke="{ORANGE}" stroke-width="36"/>'
        )
    font = 'font-family="Segoe UI, Helvetica, Arial, sans-serif" font-size="150"'
    lines.append(
        f'<text x="{sx(width / 2)}" y="{sy(0) + 220}" text-anchor="middle" fill="{NAVY}" {font}>{width:.0f}</text>'
    )
    lines.append(
        f'<text x="{sx(0) - 150}" y="{sy(height / 2)}" text-anchor="middle" fill="{NAVY}" {font} '
        f'transform="rotate(-90 {sx(0) - 150} {sy(height / 2)})">{height:.0f}</text>'
    )
    lines.append("</svg>")
    out_path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    edge = sum(1 for c in verification["cells"] if c["status"] != "RealDeficit")
    real = len(verification["cells"]) - edge
    print(f"{len(paths)} bars, {edge} edge cells, {real} deficit cells, {len(hard)} hard clashes -> {out_path}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    render(Path(sys.argv[1]), Path(sys.argv[2]))
