# OpenRebar Validation Baseline

This document is the canonical validation baseline for repository-level claims, release notes, audits, and public-facing technical documentation.

## Purpose

Use this baseline when you need to support one of the following with executable evidence:

- correctness claims in README or release notes,
- audit conclusions,
- supply-chain or dependency-governance statements,
- benchmark or report-derived engineering claims.

## Core Repository Baseline

Run these commands from the repository root unless noted otherwise:

```bash
dotnet build OpenRebar.sln --configuration Release
dotnet test OpenRebar.sln --configuration Release
```

Expected interpretation:

- `dotnet build` validates compile-time integrity for the .NET surface.
- `dotnet test` validates the current executable regression baseline.

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
python -m piptools compile --allow-unsafe --generate-hashes --output-file=requirements.locked.txt requirements.in
```

Why this exists:

- PyTorch 2.11 introduces Linux-only sidecar dependencies.
- A Windows-only local refresh can miss Linux-only hashes.
- Ubuntu CI therefore refreshes the lock before installation using the pinned `pip-tools` bootstrap.

## Optional Integrity and Governance Checks

Use these when the claim surface is broader than local correctness:

```bash
git fsck --full
dotnet list OpenRebar.sln package --include-transitive --vulnerable
dotnet list OpenRebar.sln package --outdated
dotnet format OpenRebar.sln --verify-no-changes
```

GitHub workflow and dependency-governance audit checks:

```bash
dotnet list OpenRebar.sln package --include-transitive --vulnerable
dotnet list OpenRebar.sln package --outdated
```

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

## Dependency Modernization Policy

When `dotnet list ... --outdated` shows both runtime and test-stack updates, apply modernization in two lanes:

1. Runtime lane first (application, infrastructure, host projects): prioritize security posture and production behavior.
2. Test lane second (`FluentAssertions`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`): isolate potential assertion/discovery breaking changes.

For each lane, require:

- lockfile refresh via `dotnet restore` before validation,
- at least one targeted suite proving touched behavior,
- one repository-level dependency audit (`--vulnerable` and `--outdated`) captured in commit evidence.

## Evidence Rules

- Prefer commands and outputs over narrative-only claims.
- For report-derived claims, cite the report path, schema contract, and commit SHA.
- For archived or dated documents, preserve the historical snapshot but mark it explicitly as non-current when newer evidence exists.
- If a document claims current behavior, it should reference this baseline or a stronger one.