# OpenRebar-Reinforcement: Academic Review And Execution Plan

## Scope

This review was produced from a fresh repository pass over source, tests, workflows,
contracts, and runnable entrypoints in `external/OpenRebar-reinforcement`.

## Verified Baseline

- Git working tree was clean at the start of the review.
- `dotnet test OpenRebar.sln --no-restore` passed: 114/114.
- `python -m pytest tests -q` passed in `ml/`: 4/4.

## Academic Assessment

### 1. Architecture

The repository is a successful cross-stack extraction of Clean Architecture into
C#/.NET + Revit + Python ML:

- Domain remains dependency-free and owns the behavioral contracts.
- Application orchestrates ports rather than leaking framework code inward.
- Infrastructure contains concrete adapters for parsing, optimization, export,
  reporting, and ML bridging.
- Revit integration is correctly isolated at the outer boundary.

This is not a cosmetic layering exercise. The repository actually proves the
direction of dependencies through runnable tests outside Revit.

### 2. Mathematical / normative core

The strongest academic property of the codebase is that the normative engine is
not merely documented but executable:

- SP 63 anchorage and lap-splice rules are implemented as explicit formulas.
- Reinforcement spacing and minimum reinforcement rules are encoded in pure domain
  functions.
- Domain invariants are guarded through runtime validation on key models.

That makes the repository materially stronger than typical BIM automation code,
which often embeds design rules in UI handlers, scripts, or spreadsheets.

### 3. Delivery maturity

The standalone surface is already meaningful:

- CLI entrypoint exists and can run the full pipeline without Revit.
- Canonical JSON reports, AeroBIM-friendly export, CSV schedule export, and IFC
  export are present.
- GitHub publication baseline now includes health files, CODEOWNERS, Dependabot,
  CI, CodeQL, and dependency review.

### 4. Residual risk concentration

The codebase is not risk-free. The risk is simply concentrated in a few obvious
outer-boundary surfaces rather than diffused across the system:

1. Revit production placement is only partially complete.
2. Standalone CLI usability is weaker than the core engine because its slab geometry
   contract was historically tied to a hardcoded demo footprint.
3. Release-grade supply-chain evidence is not complete yet: no SBOM, no attestation,
   no release workflow.
4. The optimizer is strong and honest, but still not a full exact branch-and-price
   implementation.

## Recommendations

### Priority A — Highest business impact

1. Finish the real Revit production boundary: tags, bending details, and host-slab
   validation.
2. Keep the standalone pipeline first-class, because it is the only environment-agnostic
   way to demonstrate correctness, run CI, and support future batch workflows.

### Priority B — Highest executable value in current environment

1. Harden the CLI public contract so standalone runs are not tied to a 30x20m demo slab.
2. Add explicit CLI integration coverage, because CLI is now a real delivery surface,
   not just a debugging helper.

### Priority C — Release and ecosystem maturity

1. Add SBOM generation and artifact attestation.
2. Add a release workflow and versioned changelog discipline.
3. Close the AeroBIM integration loop with a stable downstream contract and documented
   import path.

### Priority D — Research backlog

1. Replace the current CG master heuristic with a true LP-backed path.
2. Add benchmark datasets for cutting optimization quality.
3. Add dataset and evaluation harnesses for project-specific ML fine-tuning.

## Execution Plan

### Phase 1 — Executable now

Goal: strengthen the standalone/public surface without waiting for Revit runtime access.

Tasks:

1. Parameterize CLI slab footprint instead of hardcoding a demo geometry.
2. Validate numeric CLI input at the boundary.
3. Add CLI integration tests that verify exported artifacts and custom slab geometry.

### Phase 2 — Requires Revit SDK + live model

1. Complete `RevitRebarPlacer` tags and bending details.
2. Complete host floor extraction / validation loop in live Revit.
3. Run manual end-to-end verification in Revit 2025.

### Phase 3 — Release-grade hardening

1. SBOM generation.
2. Artifact attestation.
3. Release workflow and changelog discipline.

### Phase 4 — Research upgrades

1. LP / HiGHS-backed optimization refinement.
2. ML segmentation benchmarking and fine-tuning.

## Plan Launch Status

Phase 1 has been launched and completed: CLI hardening with configurable slab
geometry, boundary validation, and integration tests.

Phase 3 has been launched and completed: CHANGELOG.md, release workflow with
SBOM generation and artifact attestation, SBOM in CI, report schema compliance
tests, and comprehensive CLI edge-case tests.

Phase 2 (Revit SDK) and Phase 4 (research) require external infrastructure
and remain as the next execution targets.

### Executed Summary

| Item | Status | Evidence |
|------|--------|----------|
| CLI slab footprint parameterization | Done | `--slab-width`, `--slab-height` args |
| CLI numeric boundary validation | Done | `TryGetOptionalDouble` with error messages |
| CLI integration tests | Done | 8 new tests in `CliProgramIntegrationTests` |
| CHANGELOG.md | Done | Keep a Changelog format |
| Release workflow | Done | `.github/workflows/release.yml` |
| SBOM generation | Done | anchore/sbom-action in CI + release |
| Artifact attestation | Done | actions/attest-build-provenance in release |
| Report schema compliance tests | Done | 5 new tests in `ReportSchemaComplianceTests` |
| Full regression | Done | 127/127 .NET, Python ML unblocked |
