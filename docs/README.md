# OpenRebar Documentation Map

This directory is the canonical documentation entrypoint for repository-internal technical documents.

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