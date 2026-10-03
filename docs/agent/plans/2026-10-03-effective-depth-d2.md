# Plan: effective depth includes half the bar — 2026-10-03

## Context

r9 §3 item 7. The slab report stored `h - cover`. The bar axis already sits at
`cover + d/2` from the near face, so the distance to the opposite face is
`h - cover - d/2`. simple-slab is h = 220 mm, cover = 30 mm, Ø20: 190 mm becomes 180 mm.

Lengths, masses, and areas do not use this figure. They stay as they are.

## Changes

- `SlabGeometry.EffectiveDepthFor(diameterMm)` returns `h - cover - d/2`.
  A negative diameter, or a depth that is not positive, is rejected.
- `EffectiveDepthMm` stays `h - cover`. `BuildPartialReport` still uses it,
  because that path has no bar. It is a larger number than the placed-bar figure.
- `BuildReport` passes the largest placed diameter. With no bars it passes 0,
  which returns `h - cover`.
- simple-slab DXF and PNG expected reports: `effectiveDepthMm` 190 → 180.
- Tests are added to the existing `SlabGeometryTests` in `ColorLegendTests.cs`.
  A second file with the same class name does not compile.

## Acceptance

Counted suite **420/420**. The only golden change is `effectiveDepthMm`.
