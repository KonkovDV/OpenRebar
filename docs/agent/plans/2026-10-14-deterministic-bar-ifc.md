# Plan: deterministic IFC bar identity — 2026-10-14

## Context

r9 §3 item 5 starts with deterministic BarId. `PositionAssigner` already hashes
layer, direction, diameter, and the bar ends. The IFC exporter still names a bar
`BAR-{Guid}` when `Mark` is empty, and xBIM assigns a fresh `GlobalId` on every
export.

## Changes

- The reinforcing bar name is `BarId`, then `Mark`, then `BAR-{zone}-{index}`.
- The bar `GlobalId` is the IFC form of a SHA-256 taken from `BarId`, the zone,
  the index, and the bar ends. Two exports of the same bars match.
- Project, site, and relationship ids stay as xBIM assigns them. This change
  does not add `IfcReinforcingBarType` or a swept solid.

## Acceptance

One new exporter test. simple-slab lengths and masses stay as they are.
Counted suite moves from 420 to 421.
