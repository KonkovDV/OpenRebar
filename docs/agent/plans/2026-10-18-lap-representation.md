# Plan: lap representation — 2026-10-18

## Context

Lapped pieces are collinear. In IFC and Revit their bodies occupy the same
volume. r10 §4.14 adds `laps.representation`: `inPlane` or `stacked`, default
`inPlane` with `source: default`. The later piece of each lap moves by one
diameter. `lapClearance` uses the in-plan width of that pair.

## Changes

- Profile field and loader allow-list. Omitted means `inPlane` / `default`.
  `generic.json` stays as it is, so its hash and the simple-slab report stay.
- Odd lap pieces carry `LapPieceParity` 1. Drawing shifts that piece sideways
  (`inPlane`) or up (`stacked`). The design axis, the cut length, and the
  bar id stay.
- `inPlane` clearance keeps the current two-diameter width. `stacked` uses
  one diameter, because the second piece is no longer beside the first.

## Acceptance

Both clearance modes have a test. An IFC placement test sees the shift.
simple-slab figures are unchanged.
