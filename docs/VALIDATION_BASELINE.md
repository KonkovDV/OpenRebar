# OpenRebar Validation Baseline

This document is the canonical validation baseline for repository-level claims, release notes, audits, and public-facing technical documentation.

## Purpose

Use this baseline when you need to support one of the following with executable evidence:

- correctness claims in README or release notes,
- audit conclusions,
- supply-chain or dependency-governance statements,
- benchmark or report-derived engineering claims.

## Counted test total

The README claim is the sum of `test-results.trx` files written by the CI step named Test:

- `OpenRebar.Domain.Tests`, `OpenRebar.Infrastructure.Tests`, and `OpenRebar.TestCorpus` on `net8.0`
- `OpenRebar.Application.Tests` on `net8.0`
- `OpenRebar.Cli.Tests` on `net10.0`

The same library and Application tests run again on `net10.0` and write `tfm-net10-results.trx`. That file is not added to the README total. `tools/ci/verify_readme_regression_claim.py` sums every `test-results.trx` and ignores the other TRX names.

`tools/ci/compare_tfm_hashes.py` checks that net8 and net10 write the same SHA-256 for the schedule, corpus A0, and every example under `examples/`. The example hashes cover normalized `result.json`, `schedule.csv`, and `reinforcement.ifc` (no `IfcOwnerHistory`, timestamps, or GlobalId).

## Core Repository Baseline

Run these commands from the repository root unless noted otherwise:

The counted commands are the five `dotnet test` lines in the root README quick start. `dotnet test OpenRebar.sln` is not that suite: the CLI tests are .NET 10 only, and a solution-wide run does not write the `test-results.trx` files the count script sums.

```bash
# Optional benchmark summary gate (same thresholds as CI)
$env:OPENREBAR_BENCH_SUMMARY_PATH="artifacts/benchmark/column-generation-summary.json"
dotnet test tests/OpenRebar.Infrastructure.Tests/OpenRebar.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ColumnGenerationBenchmarkPackTests
```

## CI-Parity Fast Lane

Use this command sequence when you want local execution to mirror the CI `.NET` lane as closely as possible:

Restore, build, format, and the five test commands are in the root README quick start. After those tests, `python tools/ci/verify_readme_regression_claim.py` checks that both READMEs name the same passing total as the `test-results.trx` files.

## ML Baseline

Run these commands from `ml/`:

```bash
python -m pip install --require-hashes -r requirements.locked.txt
python -m pytest tests -q
```

Expected interpretation:

- dependency installation succeeds in `--require-hashes` mode,
- the committed lockfile is internally coherent for the current environment,
- the Python smoke and unit surfaces still pass.

## CI Lock-Refresh Baseline

If the change touches any of these surfaces, run the CI lock-refresh path as well:

- `ml/requirements.in`
- `ml/requirements.locked.txt`
- `.github/workflows/ci.yml`
- `.github/workflows/release.yml`
- `.github/requirements/pip-tools.locked.txt`

Run from `ml/`:

```bash
python -m pip install --require-hashes -r ..\.github\requirements\pip-tools.locked.txt
python -m piptools compile --allow-unsafe --generate-hashes --newline lf --output-file=requirements.locked.txt --pip-args "--python-version 3.11 --platform manylinux_2_28_x86_64 --platform manylinux_2_17_x86_64 --platform manylinux2014_x86_64 --implementation cp --abi cp311 --only-binary=:all:" requirements.in
```

Why this exists:

- The ML lane uses CPU PyTorch wheels (`torch==2.13.0+cpu`). CUDA packages are not in the lock.
- numpy stays on 1.26. Those wheels are tagged `manylinux_2_17`, so the refresh names that platform together with `manylinux_2_28` and `manylinux2014`.
- A Windows-only local refresh can miss Linux wheel hashes.
- CI installs the committed lock with `--require-hashes` on Python 3.11. It does not rewrite the lock.

## Optional Integrity and Governance Checks

Use these when the claim surface is broader than local correctness:

```bash
git fsck --full
dotnet list OpenRebar.sln package --include-transitive --vulnerable
dotnet list OpenRebar.sln package --outdated
dotnet format OpenRebar.sln --verify-no-changes --no-restore
```

CI runs the vulnerable list on `OpenRebar.audit.sln`, without the Revit plugin, as JSON, and `tools/ci/check_dotnet_vulnerabilities.py` fails a high finding. Each run uploads:

| Artifact | Files |
|---|---|
| `test-results` | every `test-results.trx` |
| `dependency-audit` | `deps-vulnerable.json`, `deps-outdated.txt` |
| `benchmark-summary` | `column-generation-summary.json`, `benchmark-results.trx` |

Review these control-plane files when claim scope includes CI/CD security posture:

- `.github/workflows/ci.yml`
- `.github/workflows/release.yml`
- `.github/workflows/dependency-review.yml`
- `.github/dependabot.yml`
- `.github/CODEOWNERS`

Interpretation rules:

- `git fsck --full` supports repository-integrity claims.
- vulnerability and outdated reports support supply-chain posture claims.
- formatting output is a hygiene signal, not by itself a product-correctness signal.
- workflow permission scope and Dependabot surface coverage are governance checks, not runtime correctness checks.

## Failure Triage (High-Signal)

When validation fails, use this order to reduce time-to-fix:

1. `dotnet restore` fails: check lockfile or feed connectivity first.
2. `dotnet format --verify-no-changes` fails: run `dotnet format OpenRebar.sln --no-restore`, then re-verify.
3. `dotnet test` fails: isolate the failing project/test, then rerun full baseline.
4. `verify_readme_regression_claim.py` fails: sync `README.md` and `README.ru.md` counts to actual TRX totals.
5. dependency commands fail: capture output as evidence and classify as vulnerability vs freshness issue.

## Dependency Modernization Policy

When `dotnet list ... --outdated` shows both runtime and test-stack updates, apply modernization in two lanes:

1. Runtime lane first (application, infrastructure, host projects): prioritize security posture and production behavior.
2. Test lane second (`FluentAssertions`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`): isolate potential assertion/discovery breaking changes.

For each lane, require:

- lockfile refresh via `dotnet restore` before validation,
- at least one targeted suite proving touched behavior,
- one repository-level dependency audit (`--vulnerable` and `--outdated`) captured in commit evidence.

License governance gate for test dependencies:

- Do not auto-upgrade assertion libraries if the new major version changes license terms.
- If dependency output indicates a commercial-only upgrade path for common OSS/test workflows, pin the last acceptable version and document the rationale in the commit evidence.

## Evidence Rules

- Prefer commands and outputs over narrative-only claims.
- For report-derived claims, cite the report path, schema contract, and commit SHA.
- For archived or dated documents, preserve the historical snapshot but mark it explicitly as non-current when newer evidence exists.
- If a document claims current behavior, it should reference this baseline or a stronger one.