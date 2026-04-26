# OpenRebar Execution Plan (2026-04-26)

## Scope

This plan converts the latest project audit into executable work with objective closure criteria.

## Iteration A (Current, completed in this session)

1. Stabilize README claim surface for regression counts.
2. Add a CI gate that verifies README/README.ru regression-status counters against actual TRX totals.
3. Keep the gate deterministic and cheap (single Python script, no extra dependencies).
4. Re-run full validation and publish.

## Completed Changes

- Added `tools/ci/verify_readme_regression_claim.py`.
- Wired a dedicated CI step in `.github/workflows/ci.yml`.
- Introduced machine-verifiable coupling between:
  - `README.md` regression status line,
  - `README.ru.md` regression status line,
  - actual TRX aggregate counters from `dotnet test`.

## Acceptance Criteria

1. CI fails if README claim count drifts from real test totals.
2. CI fails if EN/RU counters diverge.
3. CI passes without changing the test pipeline topology.
4. `main` remains green after merge.

## Next Iteration Candidates

1. Add canonical `examples/` fixtures (`DXF`, `PNG`) with expected `*.result.json` and `*.schedule.csv` snapshots.
2. Add benchmark artifact publication (waste/install-time distributions) with regression thresholds.
3. Add optional external IFC validator lane and publish validation report as CI artifact.
