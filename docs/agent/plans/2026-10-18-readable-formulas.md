# Plan: readable formulas in Markdown — 2026-10-18

## Context

The traceability table prints anchorage with a digit zero and a comma, and eta with a trailing digit. A reader has to guess the subscripts. `docs/archive/HYPER_DEEP_AUDIT_REPORT.md` is UTF-8 read as cp1251 and saved again, so the displayed formulas and the words inside them do not read. CIE Lab and delta-E stars are italics in Markdown.

## Changes

- Write the same symbols with Unicode subscripts in the table quotes and in the other Markdown pages: `l₀,an`, `η₁`, `Aₛ,req`, `h₀`, `1.5·h`.
- Restore the archive report from the double encoding. Do not change its numbers.
- Escape the CIE Lab and delta-E stars so they stay stars.

## Acceptance

`NormativeProfilesTests` still matches `docs/NORMATIVE_TRACEABILITY.md` to `NormativeTraceability.Render()`. No golden figure changes.
