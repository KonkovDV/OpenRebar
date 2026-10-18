# Plan: public docs in plain language — 2026-10-18

## Context

The root README accumulated one bullet per landed change. The docs map
talks about claim hierarchies. The SOTA note still opens as a "hyper-deep
analysis" and still says the cutter is FFD and that IFC has no bar body.

## Changes

- Rewrite `README.md` and `README.ru.md` as the same page: what the program
  does, who writes the required area, what AeroBIM checks, how to run the
  regression, and the limits a user hits. Keep **467/467**.
- Point at `docs/SOTA_2026.md` in one sentence. Do not paste the gap table.
- Shorten `docs/README.md` to a map.
- Correct the architecture cutter name and the ML paragraph.
- Correct the SOTA opening and the summary rows that the code has passed.

## Acceptance

`verify_readme_regression_claim.py` passes. No golden figures change.
