# Plan: oblique slab outline — 2026-10-18

## Context

`SlabEdges.WorkingRectangle` builds the working area from the bounding box.
A Supported or Continuous edge on a non-rectangular outline was therefore
applied to a rectangle the drawing does not have. r10 §4.8 says to stop that
with `edgesRequireRectangularSlab` (exit 3). Free edges stay on the real
outline. An oblique row is still quantized outward by 10 mm, then clipped so
the end stays inside the contour within 0.1 mm.

## Changes

- Domain: an axis-aligned rectangle is every edge horizontal or vertical and
  the area equal to the bounding box. Anything else with a declared support
  throws from `WorkingRectangle` and does not return the box.
- Pipeline: the same case is a critical diagnostic named
  `edgesRequireRectangularSlab` before any bar is placed. Exit code 3.
- `BarRunBuilder`: after the outward 10 mm step, clip the interval back to
  the contour cut.

## Acceptance

Existing rectangular examples are unchanged. New tests cover the refusal, the
exit code, and an oblique end inside the contour. README count follows the TRX.
