# Plan: closing bar keeps the clear gap — 2026-10-20

## Context

On the house slab the top two rows sit at y = 3865 and 3885 mm. The second
one is the extra station `CoveringStations` adds when the last grid bar does
not cover the far edge. The centres are 20 mm apart. For two Ø20 bottom bars
the clear gap required by the table already in the repo is `max(d, 25 mm)`,
so the centres must be at least 45 mm apart. The checker records
`layerClearance` and the layout still places both bars.

## Rule

The requested spacing stays. The extra station at `max − spacing/2` is kept
when it is at least the minimum centre distance from the last grid bar.
When it is closer, and the bar before that still clears the minimum, the
last grid bar moves to the extra station. One bar covers the edge. Steel is
not placed through steel. The minimum is the same clear gap the checker
already uses, for two bars of the zone diameter.

## Acceptance

- A 6000 × 4000 slab, Ø20 at 150 mm, cover 30 mm, places 26 bars. The last
  is at 3885 mm. Every gap is at least 45 mm.
- A closing gap that already clears the minimum still adds the extra bar.
- simple-slab drops the `layerClearance` clash. Length of one bar is
  unchanged. Count, mass, purchase, and the plan follow the removed row.
  `schemaVersion` stays 2.1.0. The counted README phrase follows the TRX.

## Measured on simple-slab

Count 27 → 26. Last ordinate 3885 mm, previous gap 170 mm. One bar stays
6305.486677646163 mm. Installed mass 420.51290653222264 → 404.9383544384366 kg.
Purchased mass 600.21 → 577.98 kg. Waste percent stays 29.905703581709297.
`layerClearance` is gone. 26 clashes `hookOutsideConcrete` remain.
`excessSteelKg` 11.08579098744178 → 0. Areas: edge 5.37 → 5.46, real deficit
2.52 → 2.55, under-reinforced 7.89 → 8.01, cells 3156 → 3204.
