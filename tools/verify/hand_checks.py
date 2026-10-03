#!/usr/bin/env python3
"""Independent hand checks of the SP 63 tables and the example reports.

Runs without .NET. It recomputes anchorage and lap lengths from the formulas of
SP 63.13330.2018 clauses 10.3.24 and 10.3.30, the bend arc from clause 10.3.33, and the
length, mass and waste of the example reports. Exit code 1 means a mismatch.
"""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TABLES = ROOT / "src/OpenRebar.Domain/Rules/Data/ru.sp63.2018.tables.v3.json"

# Hand values, worked on paper: (d, steel, concrete) -> (l_an, lap50, lap100), mm.
# l0,an = Rs*d / (4*eta1*eta2*Rbt). Rounded up to 10 mm.
HAND = {
    (12, "A500C", "B25"): (500, 600, 1000),   # l0 = 435*12/10.5 = 497.1
    (20, "A500C", "B25"): (830, 1000, 1660),  # l0 = 435*20/10.5 = 828.6
    (16, "A400", "B30"): (480, 570, 950),     # l0 = 340*16/11.5 = 473.0
    (10, "B500", "B25"): (500, 600, 990),     # l0 = 415*10/8.4 = 494.0
    (36, "A500C", "B25"): (1660, 1990, 3320), # eta2 = 0.9: l0 = 435*36/9.45 = 1657.1
}


def ceil10(value: float) -> float:
    return math.ceil(value / 10.0 - 1e-9) * 10.0


def basic(t: dict, d: int, steel: str, concrete: str) -> float:
    kind = t["barTypeByClass"][steel]
    eta1 = t["eta1"][kind]
    eta2 = t["eta2"]["upTo32Mm"] if d <= 32 else t["eta2"]["from36Mm"]
    rbt = t["bondStressByConcreteClass"][concrete]
    rs = t["designStrengthBySteelClass"][steel]
    return rs * d / (4.0 * eta1 * eta2 * rbt)


def anchorage(t: dict, d: int, steel: str, concrete: str) -> float:
    a = t["anchorage"]
    l0 = basic(t, d, steel, concrete)
    need = a["tensionAlpha"] * l0
    low = max(a["minimumFactorOfBasic"] * l0, a["minimumDiameters"] * d, a["minimumMm"])
    return ceil10(max(need, low))


def lap(t: dict, d: int, steel: str, concrete: str, alpha: float) -> float:
    p = t["lap"]
    l0 = basic(t, d, steel, concrete)
    low = max(p["minimumFactorOfAlphaBasic"] * alpha * l0, p["minimumDiameters"] * d, p["minimumMm"])
    return ceil10(max(alpha * l0, low))


def hook_arc(t: dict, d: int, steel: str, ends: int) -> float:
    m = t["mandrel"]
    periodic = t["barTypeByClass"].get(steel, "hotRolledPeriodic") != "smooth"
    below = d < m["splitDiameterMm"]
    factor = (m["periodicBelowSplit"] if below else m["periodicFromSplit"]) if periodic else (
        m["smoothBelowSplit"] if below else m["smoothFromSplit"])
    return ends * math.pi * (factor * d / 2.0 + d / 2.0)


def main() -> int:
    t = json.loads(TABLES.read_text(encoding="utf-8"))
    errors: list[str] = []
    for (d, steel, concrete), (an, l50, l100) in HAND.items():
        got = (
            anchorage(t, d, steel, concrete),
            lap(t, d, steel, concrete, t["lap"]["upTo50Alpha"]),
            lap(t, d, steel, concrete, t["lap"]["full100Alpha"]),
        )
        status = "ok" if got == (an, l50, l100) else "MISMATCH"
        print(f"d{d} {steel} {concrete}: l_an/lap50/lap100 = {got} hand {(an, l50, l100)} {status}")
        if status != "ok":
            errors.append(f"d{d} {steel} {concrete}")

    for report in sorted(ROOT.glob("examples/**/expected/*.result.json")):
        r = json.loads(report.read_text(encoding="utf-8"))
        mass = t["linearMassKgPerM"]
        for zone in r["zones"]:
            d = zone["diameterMm"]
            n = zone["rebarCount"]
            span = zone["totalClearSpanMm"] / n
            arc = hook_arc(t, d, "A500C", 2)
            expected = n * (span + arc)
            ok = abs(expected - zone["totalLengthMm"]) < 0.01
            print(f"{report.relative_to(ROOT)} {zone['zoneId']}: length {zone['totalLengthMm']:.2f} hand {expected:.2f} {'ok' if ok else 'MISMATCH'}")
            if not ok:
                errors.append(f"{report.name} length")
            kg = zone["totalLengthMm"] / 1000.0 * mass[str(d)]
            print(f"  mass from exact length {kg:.3f} kg, summary {r['summary']['totalMassKg']:.3f} kg")
        positions = sum(p["totalMassKg"] for p in r["positions"])
        drift = r["summary"]["totalMassKg"] - positions
        print(f"  positions mass {positions:.6f} kg, drift vs summary {drift:+.6f} kg")
        if abs(drift) > 1e-6:
            errors.append(f"{report.name} mass drift {drift:+.6f} kg")
        print(f"  waste {r['summary']['totalWastePercent']:.2f}%, purchased {r['summary']['massPurchasedKg']} kg")
    if errors:
        print("FAILED:", ", ".join(errors))
        return 1
    print("all hand checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
