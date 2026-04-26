# OpenRebar Documentation Map

This directory is the canonical documentation entrypoint for repository-internal technical documents.

## Documentation Quality Principles

- Keep one canonical source per invariant and avoid content forks.
- Separate current-state claims from historical evidence snapshots.
- Tie normative or quality claims to executable checks.
- Keep EN/RU public claim surfaces synchronized when test status or workflow guidance changes.

## Document Classes

OpenRebar distinguishes between three documentation classes:

| Class | Purpose | Files |
|---|---|---|
| Canonical reference | Stable technical understanding of the system as it exists now | `architecture.md`, `NORMATIVE_TRACEABILITY.md`, `VALIDATION_BASELINE.md` |
| Evidence and audits | Time-bounded verification snapshots, findings, and remediation evidence | `COMPREHENSIVE_PROJECT_AUDIT_2026_04_25.md`, `HYPER_DEEP_AUDIT_REPORT.md` |
| Roadmaps and execution plans | Planned work, historical execution intent, and backlog framing | `ACADEMIC_REVIEW_AND_EXECUTION_PLAN_2026_04_12.md`, `OPENREBAR_EXECUTION_PLAN_2026_04_26.md`, `TASKS.md` |

## Read By Goal

### Understand the current system

- [architecture.md](architecture.md): layer boundaries, data flow, domain ports, optimization architecture, and ML boundary
- [NORMATIVE_TRACEABILITY.md](NORMATIVE_TRACEABILITY.md): clause-to-code and clause-to-test traceability for the normative core
- [VALIDATION_BASELINE.md](VALIDATION_BASELINE.md): canonical executable validation baseline for repository, ML, and CI lock-refresh claims

### Review repository quality and evidence

- [COMPREHENSIVE_PROJECT_AUDIT_2026_04_25.md](COMPREHENSIVE_PROJECT_AUDIT_2026_04_25.md): latest repository-wide audit with evidence snapshot and remediation status
- [HYPER_DEEP_AUDIT_REPORT.md](HYPER_DEEP_AUDIT_REPORT.md): deeper architectural and academic audit narrative, including integration framing
- [.github/workflows/release.yml](../.github/workflows/release.yml), [.github/dependabot.yml](../.github/dependabot.yml), and [VALIDATION_BASELINE.md](VALIDATION_BASELINE.md): operational governance surfaces for CI token scope and dependency-update coverage

### Understand roadmap and planned work

- [ACADEMIC_REVIEW_AND_EXECUTION_PLAN_2026_04_12.md](ACADEMIC_REVIEW_AND_EXECUTION_PLAN_2026_04_12.md): review-driven execution framing and phased recommendations
- [OPENREBAR_EXECUTION_PLAN_2026_04_26.md](OPENREBAR_EXECUTION_PLAN_2026_04_26.md): active April 2026 execution wave (CI claim gate, examples/snapshots, benchmark/dependency artifacts)
- [TASKS.md](TASKS.md): detailed work backlog, implementation notes, and historical execution plan

## Scope Rules

- Root-level markdown is reserved for repository-facing governance and community-health surfaces such as `README.md`, `CONTRIBUTING.md`, `SECURITY.md`, and `CHANGELOG.md`.
- Technical audits, execution plans, and roadmap-heavy materials belong under `docs/`.
- Evidence documents are snapshots, not living source-of-truth contracts unless explicitly stated.
- When behavior changes, update the smallest canonical surface first, then align supporting evidence or roadmap docs only if they encode the same invariant.

## Claim Surface Hierarchy

Use this order when updating or validating documentation claims:

1. `README.md` + `README.ru.md` (public current-state claim surface)
2. `docs/VALIDATION_BASELINE.md` (canonical executable evidence baseline)
3. `docs/architecture.md` and `docs/NORMATIVE_TRACEABILITY.md` (stable technical reference)
4. Dated audits and plans in `docs/` (historical evidence and roadmap context)

If a statement conflicts across levels, update the lower-priority document to match the higher-priority canonical source.

## Verification Baseline

Use this baseline before strengthening claims in docs, audits, or release notes:

```bash
dotnet build OpenRebar.sln --configuration Release
dotnet test OpenRebar.sln --configuration Release
cd ml
python -m pip install --require-hashes -r requirements.locked.txt
python -m pytest tests -q
```

If the change touches ML dependency governance or workflow behavior, also verify the pinned lock-refresh path used in CI:

```bash
python -m pip install --require-hashes -r ..\.github\requirements\pip-tools.locked.txt
python -m piptools compile --allow-unsafe --generate-hashes --output-file=requirements.locked.txt requirements.in
```

## Documentation PR Checklist

Before merging docs changes:

1. Verify that all current-state numbers in `README.md` and `README.ru.md` match executable evidence.
2. Ensure every new command snippet is runnable as written (paths, flags, OS notes).
3. Mark historical values with explicit date context.
4. Avoid duplicating long protocol text across multiple files; link to the canonical file.
5. For CI-related claims, confirm corresponding workflow paths in `.github/workflows/`.