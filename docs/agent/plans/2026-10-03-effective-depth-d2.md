# Plan: fix EffectiveDepthFor — 2026-10-03

## Context

r9 §3 item 7: `SlabGeometry.EffectiveDepthMm` returns `h − cover`, but the
structural effective depth is `h − cover − d/2` (the bar axis is at cover + d/2
from the face). For the `simple-slab` etalon (h=220 mm, cover=30 mm, Ø20):
- Was:  220 − 30 = **190 mm** ❌
- Correct: 220 − 30 − 10 = **180 mm** ✅

Ref: SP 63.13330.2018 §10.3 (definition of working height h₀).

## Changes

| File | Action |
|---|---|
| `SlabGeometry.cs` | Add `EffectiveDepthFor(int diameterMm)` method; keep `EffectiveDepthMm` for partial reports |
| `GenerateReinforcementPipeline.cs` | `BuildReport` uses `EffectiveDepthFor(maxDiameter)`; `BuildPartialReport` unchanged |
| `examples/png/simple-slab/expected/input.result.json` | `effectiveDepthMm: 190` → `180` |
| `examples/dxf/simple-slab/expected/input.result.json` | `effectiveDepthMm: 190` → `180` |
| `tests/.../SlabGeometryTests.cs` | New: tests for `EffectiveDepthFor` and backward-compat property |

## Why keep `EffectiveDepthMm`?

`BuildPartialReport` is called when the pipeline aborts before bars are computed
(parse failure, layer error, etc.). At that stage the bar diameter is unknown.
`EffectiveDepthMm = h − cover` is a safe conservative fallback that is explicitly
documented as a partial-report value.

## Acceptance

- `SlabGeometryTests` green
- Etalon hashes accepted by `ExampleArtifactHashTests`
- `dotnet test` count stays at 416 + 3 new = **419/419**
