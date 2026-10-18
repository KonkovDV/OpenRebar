# Plan: effective depth by layer — 2026-10-18

## Context

The report has one `effectiveDepthMm`, from the largest placed bar. r10 §4.9
asks for `slab.effectiveDepthByLayer` on the layers that actually have bars.
The axis elevation already lives in `LayerElevations`. Bottom depth is
`h − z_axis`. Top depth is `z_axis`. `effectiveDepthMm` stays. Schema stays
2.1.0 because that version is not released.

## Changes

- Domain: build the map from placed bars. A layer with no bar is absent.
  Both covers are the slab cover. The report has no separate top cover.
- Schema: optional object, keys `BottomX`, `BottomY`, `TopX`, `TopY`.
- simple-slab DXF and PNG: `BottomX` 180. That is `220 − 30 − 20/2`.
  Lengths, masses, and `effectiveDepthMm` stay.

## Acceptance

Domain tests match the existing axis elevations 33/47/173/187. Example
snapshots match. README count follows the TRX.
