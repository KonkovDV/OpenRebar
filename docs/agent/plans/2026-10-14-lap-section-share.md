# Plan: lap share over the whole section — 2026-10-14

## Context

r10 RT-5, and the same open row in `docs/SOTA_2026.md`. `LapPlanner` counts a
splice only inside the group of bars that share the same ends. Two neighbouring
groups can each stay under half and still splice more than half the bars in
one section. The section is 1.3 lap lengths, the factor already in
`tables.v3.json`. No new alpha.

## Changes

- `LapSectionShare` is the one ratio: denominator = bars of the layer whose
  axis crosses the window, numerator = those with a splice in that window.
- `LapPlanner` and `ClearanceChecker` both call it.
- A splice sized with the up-to-50% lap, in a section that is over half,
  is a hard clash `lapShareExceeded` with the bar ids. The full-section lap
  length is not that clash.
- simple-slab has no laps, so the examples do not change.

## Acceptance

Two clearance facts: three of four bars spliced in one window with the
up-to-50% length is `lapShareExceeded`; the same splices more than 1.3 lap
lengths apart are not. Existing lap tests stay green.
