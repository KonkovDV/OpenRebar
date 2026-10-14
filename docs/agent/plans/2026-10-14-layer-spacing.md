# Plan: combined-layer spacing — 2026-10-14

## Context

r9 RT-2. The zone calculator warned when one zone's own spacing was wider than
the limit already in `ReinforcementLimits.MaxSpacing`. Additional bars sit
between the background mesh, so their own spacing can be wider than that limit.

## Changes

- The calculator no longer warns about one zone's spacing.
- `ClearanceChecker` measures the gap to the nearest parallel bar of the same
  face and direction whose length overlaps. A gap wider than the existing limit
  is a hard clash `layerSpacing`.
- A hard clash still does not set verification to Failed. The area check remains
  the gate.

## Acceptance

Two clearance facts and the calculator test. simple-slab spacing is 150 mm and
the limit at 220 mm thickness is 330 mm, so the examples do not change.
Counted suite moves from 421 to 423.
