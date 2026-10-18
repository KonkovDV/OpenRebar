# Plan: correct the subscript sweep — 2026-10-18

## Context

The sweep that made formulas readable also wrote rare subscript letters (`l` with a subscript `an`, a subscript `l`, `mu` with a subscript `min`) into historical plans, and renamed the stop-question symbol `Rbt` to `R_bt`. Those letters do not render in a normal font.

## Changes

- Put the traceability quotes back in HTML subscripts, which the browser draws.
- Restore the plan pages, the SOTA note's symbol names, the triage note, and the architecture line to the wording from before that sweep.
- Keep the restored April audit text and the division spacing on simple-slab.

## Acceptance

`NormativeProfilesTests` matches the traceability note. No golden figure changes.
