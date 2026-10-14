# Plan: DXF entities are not dropped silently — 2026-10-14

## Context

r10 PR-11. A closed polyline with no colour, a colour outside the legend, or an
entity that is not a polyline or hatch returned without a reason. The existing
counters `unsupportedCurve`, `ignoredOpenPolylines`, and
`unsupportedBoundaryEdge` stay as they are.

## Changes

- Those three cases write `noColor`, `colorNotInLegend`, and
  `unsupportedEntity:<type>` into `ignoredByReason`.
- A boundary with fewer than two vertices writes `degenerateBoundary`.
- `colorNotInLegend` is also a pipeline warning.
- simple-slab is one closed polyline in the legend, so its `ignoredByReason`
  stays `unitsAssumedMillimetres`.

## Acceptance

One parser fact per new reason. Counted suite moves from 441 by those facts.
CI is green.
