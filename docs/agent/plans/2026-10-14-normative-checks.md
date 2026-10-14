# Plan: publish check status — 2026-10-14

## Context

r10 C-1. Crack width and deflection are not calculated from a required-area
input, and the report did not say so. A new top-level field needs schema 2.1.0.

## Changes

- `checks` lists `providedArea`, `crackWidth`, and `deflection`.
- `providedArea` is `Passed` when verification status starts with `Passed`,
  `Failed` otherwise, and `NotEvaluated` when verification did not run.
- Crack width and deflection are `NotEvaluated` with reason
  `inputIsRequiredAreaOnly`.
- `transverseInLap` is written only when the layout has a lap, reason `Q-2b`.
- simple-slab has no laps and a failed area check, so the three entries are
  `Failed`, `NotEvaluated`, `NotEvaluated`. No other golden figure changes.

## Acceptance

Domain facts for the four status cases and for a lap. Both example reports
validate. README states the limitation in English and Russian.
