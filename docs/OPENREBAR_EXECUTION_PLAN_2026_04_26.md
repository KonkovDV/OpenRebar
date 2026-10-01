# OpenRebar Execution Plan (2026-04-26)

## Scope

This plan converts the latest project audit into executable work with objective closure criteria.

## Iteration A (completed)

1. Stabilize README claim surface for regression counts.
2. Add a CI gate that verifies README/README.ru regression-status counters against actual TRX totals.
3. Keep the gate deterministic and cheap (single Python script, no extra dependencies).
4. Re-run full validation and publish.

## Iteration B (completed)

1. Normalize formatting drift to restore `dotnet format --verify-no-changes` green lane.
2. Introduce line-ending normalization guardrails via repository-level `.gitattributes`.
3. Preserve behavior while keeping the formatter PR scope style-only.

## Iteration C (completed)

1. Add CI dependency-governance artifacts (`--vulnerable`, `--outdated`) as non-blocking evidence.
2. Add canonical `examples/` fixtures for DXF and PNG with committed expected snapshots (`result.json`, `schedule.csv`).
3. Add snapshot integration tests that execute CLI examples and compare outputs to expected fixtures.
4. Add benchmark summary export and CI artifact publication with threshold-gated benchmark validation.
5. Add optional IFC validation lane (`workflow_dispatch`) with machine-readable report artifact.
6. Sync docs surfaces to reflect implemented CI gates and examples workflow.

## Completed Changes

- Added `tools/ci/verify_readme_regression_claim.py`.
- Wired a dedicated CI step in `.github/workflows/ci.yml`.
- Added CI dependency artifact generation and upload (`dependency-audit`).
- Added CI benchmark summary gate and artifact upload (`benchmark-summary`).
- Added `.gitattributes` for EOL normalization.
- Added canonical examples and expected snapshots:
  - `examples/dxf/simple-slab/`
  - `examples/png/simple-slab/`
- Added snapshot tests: `tests/OpenRebar.Application.Tests/ExamplesSnapshotTests.cs`.
- Added example regeneration scripts:
  - `tools/examples/generate_expected_outputs.ps1`
  - `tools/examples/generate_expected_outputs.sh`
  - `tools/examples/generate_png_example.py`
- Added benchmark summary export path in `ColumnGenerationBenchmarkPackTests` via `OPENREBAR_BENCH_SUMMARY_PATH`.
- Added optional IFC validation lane:
  - `tools/ci/validate_ifc_examples.py`
  - `ifc-validation` job in `.github/workflows/ci.yml` (`workflow_dispatch` only)
- Introduced machine-verifiable coupling between:
  - `README.md` regression status line,
  - `README.ru.md` regression status line,
  - actual TRX aggregate counters from `dotnet test`.
## Acceptance Criteria

1. CI fails if README claim count drifts from real test totals.
2. CI fails if EN/RU counters diverge.
3. CI passes without changing the test pipeline topology.
4. `main` remains green after merge.

## Next Wave

1. Run manual P0 GitHub admin enablement checklist (rulesets, security toggles) outside codebase.
2. Complete P1 real Revit 2025 end-to-end evidence pack with reproducible artifacts (input, result, schedule, logs, screenshots, Revit build/version, OpenRebar commit SHA).
3. Add production batch corpus integration policy for private data rails:
  - keep only manifest schema/example in public repo,
  - document `OPENREBAR_BATCH_CORPUS_ROOT` hookup,
  - define benchmark gating metrics and fail-threshold policy.
4. Optionally replace IFC sanity lane with a heavier external validator in nightly mode.
