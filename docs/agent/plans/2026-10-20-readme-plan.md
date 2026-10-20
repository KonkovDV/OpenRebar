# Plan: show the house slab, fix the pages that drifted — 2026-10-20

## Context

The README describes the house slab in numbers only. A run already writes everything a plan needs: the IFC bar axes, the 50 mm verification cells, and the clashes. The logo PNG carries an opaque light box, which reads as a sticker on GitHub's dark theme.

Several pages say things the code does not do. `ml/SUPPLY_CHAIN_SECURITY.md` says CI regenerates the lock and links a model release that does not exist. `ml/data/README.md` is written as `#` comment lines, so GitHub renders it as headings. `SECURITY.md` points to a noreply address and to private reporting that was off. The CLI help still says `--include-diagnostics` writes the stack into the report.

## Changes

- `tools/examples/render_plan.py` draws the plan from a run folder. `docs/images/simple-slab-plan.svg` is that plan for the house slab.
- `tools/brand/make_logo.py` cuts two transparent lockups from the brand sheet. The README uses them through `<picture>`.
- Align `SECURITY.md`, `CODE_OF_CONDUCT.md`, `CONTRIBUTING.md`, `docs/architecture.md`, `docs/VALIDATION_BASELINE.md`, `.github/BRANCH_PROTECTION_CHECKLIST.md`, `ml/SUPPLY_CHAIN_SECURITY.md`, `ml/data/README.md`, and `CITATION.cff` with the code.
- Reorder the CLI help: inputs, slab, rules and output, then exit codes. Fix the diagnostics and ML lines.
- Record what this pass found in `docs/AUDIT_2026-10-20.md`.

## Acceptance

Both **467/467** phrases stay. No golden figure changes. `dotnet format --verify-no-changes` passes.
