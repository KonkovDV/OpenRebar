# Plan: hook crown on the cover line — 2026-10-20

## Context

On the house slab the straight end stops 40 mm from a free face
(`max(coverEdgeMm, coverMm + d/2)` for Ø20). The hook is a semicircle of the
centerline radius from clause 10.3.33, 90 mm, and that crown reaches 50 mm
past the face. `docs/agent/plans/2026-10-02-p0-r9b.md` left the curve for a
later task. The mandrel is not reduced, hook credit stays 0, and no U-bar is
generated.

## Rule

The crown of a `NeedsHook` end lies one centerline radius past the straight
joint, toward the free side. The joint moves in by that radius when the bar
stays longer than the fit tolerance, so the crown lands on the cover line the
straight axis already uses. The bar surface at the crown then has `coverMm`
to the face. A span too short for the reach is left where the anchorage
extender put it, so the bar is not inverted.

The same semicircle still rises `2 × radius` from the axis. On the house slab
the axis is at 40 mm and the rise is 180 mm, so the axis meets the top face
and the bar surface crosses it by 10 mm. That is a hard clash
`hookOutsideConcrete`, one per bar. The plan drawing does not ring it: the
point is inside the slab in plan, and the exit is through the top face.

## Acceptance

- A free hooked end on the existing tests sits one centerline radius inside
  the cover line. A supported end is unchanged.
- A hook whose surface stays inside the concrete is not a clash. A crown past
  the face, or a semicircle through the top face, is one hard clash.
- A span shorter than the two reaches keeps a positive length.
- simple-slab lengths, masses, waste, area figures, and the plan image follow
  the shorter straight part. Purchased mass stays one 9000 mm bar per piece.
  `schemaVersion` stays 2.1.0. The counted README phrase follows the TRX.

## Measured on simple-slab

Straight length 5920 → 5740 mm (90 mm at each end, the Ø20 centerline radius).
Developed length 175108.14 → 170248.14 mm. Catalogue length 6485 → 6305 mm.
Installed mass 432.5171065322226 → 420.51290653222264 kg. Purchased mass 600.21 kg.
Waste 27.9057 % → 29.9057 %. `edgeDevelopmentAreaM2` 6.12 → 5.37,
`realDeficitAreaM2` 0.99 → 2.52, `underReinforcedAreaM2` 7.11 → 7.89,
cells 2844 → 3156. `minMarginMm2PerM` is unchanged. 27 clashes
`hookOutsideConcrete` at the start joint, plus the existing `layerClearance`.
