# OpenRebar — Reinforcement Automation for RC Slabs

[![CI](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml/badge.svg)](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/KonkovDV/OpenRebar/badge)](https://scorecard.dev/viewer/?uri=github.com/KonkovDV/OpenRebar)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

English | [Русский](README.ru.md)

OpenRebar generates reinforcement layouts for flat RC slabs from isoline-based inputs (DXF or PNG). The core libraries build for .NET 8 and .NET 10. The CLI ships on .NET 10. The engineering logic stays testable outside Revit. The Revit add-in is two builds, chosen by the Revit process runtime: `RevitNet8` when that runtime is still .NET 8, and `RevitNet10` for .NET 10.

This repo ships three execution surfaces:

- **CLI host** (`src/OpenRebar.Cli`) — batch runs, CI, debugging, integration artifacts
- **Revit host** (`src/OpenRebar.RevitPlugin`) — external command + UI (compiled only when Revit SDK references are available)
- **Optional ML module** (`ml/`) — U-Net segmentation for PNG isolines exposed via HTTP (FastAPI)

## Table of Contents

- [Problem Statement](#problem-statement)
- [Quick Start (CI-Parity)](#quick-start-ci-parity)
- [Outputs and Integration Contract](#outputs-and-integration-contract)
- [Architecture](#architecture)
- [Domain Ports](#domain-ports)
- [Build and Test](#build-and-test)
- [CI Quality Gates](#ci-quality-gates)
- [History](#history)
- [Known limitations](#known-limitations)
- [Canonical Examples and Snapshots](#canonical-examples-and-snapshots)
- [Python ML Module (Optional)](#python-ml-module-optional)
- [Revit Host Boundary](#revit-host-boundary)
- [Project Docs](#project-docs)
- [Scientific Reporting Standard](#scientific-reporting-standard)

## Quick Start (CI-Parity)

Prerequisites:

- .NET SDK 8.x and .NET SDK 10.x (the CLI and the `net10.0` libraries need the 10 SDK; the Revit .NET 8 build needs the 8 SDK)
- Python 3.11+ (for `tools/ci/*.py` and optional `ml/` lane)
- Git with LF-aware checkout

Run from repository root:

```bash
dotnet restore OpenRebar.sln --locked-mode -p:EnableWindowsTargeting=true -p:Platform=x64
dotnet build OpenRebar.sln --no-restore --configuration Release -p:EnableWindowsTargeting=true -p:Platform=x64
dotnet format OpenRebar.sln --verify-no-changes --no-restore
dotnet test tests/OpenRebar.Domain.Tests/OpenRebar.Domain.Tests.csproj --no-build --configuration Release -f net8.0
dotnet test tests/OpenRebar.Infrastructure.Tests/OpenRebar.Infrastructure.Tests.csproj --no-build --configuration Release -f net8.0
dotnet test tests/OpenRebar.TestCorpus/OpenRebar.TestCorpus.csproj --no-build --configuration Release -f net8.0
dotnet test tests/OpenRebar.Application.Tests/OpenRebar.Application.Tests.csproj --no-build --configuration Release -f net8.0
dotnet test tests/OpenRebar.Cli.Tests/OpenRebar.Cli.Tests.csproj --no-build --configuration Release -f net10.0
python tools/ci/verify_readme_regression_claim.py
```

This sequence mirrors the core .NET lane in CI and is the recommended pre-PR minimum for code or docs claims touching test status.

## Problem Statement

The motivating workflow is reinforcement placement in Revit from isoline maps exported by tools such as LIRA-SAPR / Stark-ES. Manual placement is time-consuming and susceptible to inconsistency across floors, zones, and engineers.

OpenRebar implements a reproducible pipeline:

1. Parse an isoline file (**DXF** or **PNG**) into color-coded reinforcement zones
2. Classify zones and decompose complex polygons into rectangles (with persisted coverage/over-coverage evidence)
3. Calculate reinforcement layout per zone (spacing, diameter, anchorage rules)
4. Optimize cutting (1D CSP) to reduce waste (exact small-instance path + column-generation baseline for larger instances)
5. Persist auditable machine-readable artifacts for downstream BIM systems
6. (When enabled) place straight Rebar elements in Revit and generate tags. Bent bars are refused with an error until bent shapes are supported

The target is to reduce routine slab reinforcement placement from engineer-weeks to engineer-hours **under validated assumptions** and with a stable, reviewable output contract.

## Outputs and Integration Contract

The repository treats the machine-readable outputs as first-class artifacts.

| Artifact | Produced by | Purpose |
|---|---|---|
| `*.result.json` | Pipeline / CLI | Canonical reinforcement execution report (schema-backed) |
| `*.aerobim.json` | CLI exporter | AeroBIM-oriented summary export |
| `*.schedule.csv` | CLI exporter | Rebar schedule export for downstream spreadsheets/ERP |
| `*.reinforcement.ifc` | CLI exporter | IFC export surface (IFC4 via xBIM) |

**Canonical schema:** `contracts/aerobim-reinforcement-report.schema.json` (`schemaVersion` `2.0.0`)

The canonical report explicitly persists:

- `normativeProfile` (for example `ru.sp63.2018`) and a versioned table-set id (for example `ru.sp63.2018.tables.v3`)
- `analysisProvenance` for geometry decomposition and cutting optimisation (algorithm ids, thresholds, and fallbacks)
- per-cutting-plan `sawCutWidthMm`, so downstream consumers can independently recompute kerf-aware `wasteMm` / `wastePercent`
- per-diameter `dualBound` / `gap` quality-bound telemetry when available from the optimizer; heuristic fallback-master runs intentionally suppress these fields (`null`) because LP lower-bound guarantees do not apply

## Architecture

Clean Architecture with strict dependency inversion:

```
Domain (pure) ← Application (use cases) ← Infrastructure (adapters) ← Hosts (CLI, RevitPlugin)
```

- **Domain** (`src/OpenRebar.Domain`): models, ports (interfaces), rules; no external dependencies
- **Application** (`src/OpenRebar.Application`): orchestration and use cases; depends only on Domain
- **Infrastructure** (`src/OpenRebar.Infrastructure`): DXF/PNG parsing, optimisation, exports, report store, logging adapter; depends on Domain + Application
- **Hosts**:
	- CLI (`src/OpenRebar.Cli`)
	- Revit plugin (`src/OpenRebar.RevitPlugin`, `#if REVIT_SDK`)

## Domain Ports

Ports are defined in `src/OpenRebar.Domain/Ports/`.

| Port | Responsibility |
|---|---|
| `IIsolineParser` | Parse DXF/PNG isolines into zones |
| `ILegendLoader` | Load legend configuration or provide defaults |
| `IZoneDetector` | Zone classification and polygon decomposition |
| `IReinforcementCalculator` | Generate rebar segments per zone |
| `IRebarOptimizer` | Cutting stock optimisation (1D CSP) |
| `ISupplierCatalogLoader` | Load available stock lengths / pricing |
| `IReportStore` | Persist canonical `*.result.json` execution reports |
| `IReportExporter` | Export external-system reports (for example AeroBIM) |
| `IScheduleExporter` | Export rebar schedule (CSV) |
| `IIfcExporter` | Export IFC artefacts |
| `IRevitPlacer` | Place rebars in an active Revit document |
| `IImageSegmentationService` | ML-backed PNG segmentation bridge (HTTP) |
| `IStructuredLogger` | Minimal structured logging abstraction (host-friendly) |

## Key Implemented Surfaces

### Normative Engine (SP 63.13330.2018)

The current normative basis is packaged as a versioned embedded profile resource, with golden tests covering:

- bond stress / design strength lookup
- periodic-profile classification
- linear mass tables

### Cutting Optimisation

Two optimiser implementations behind `IRebarOptimizer`:

| Algorithm | Intended role |
|---|---|
| `ColumnGenerationOptimizer` | Production-oriented baseline with persisted optimisation provenance and an exact small-instance fast path |
| `FirstFitDecreasingOptimizer` | Heuristic baseline and fallback |

The column-generation implementation should be understood as a strong engineering baseline (LP/pricing/repair), not as a mathematically complete branch-and-price solver. This distinction is persisted into the canonical report provenance.
Reported `WasteMm` / `WastePercent` are kerf-aware: they measure residual stock after both installed rebar length and saw-cut material loss are subtracted.

### DXF/PNG Color Recognition

- **DXF:** AutoCAD ACI palette (256 colors) + ByLayer resolution
- **PNG:** exact RGB match against the legend in the deterministic path; anti-aliased edge pixels stay unlabeled. CIE L\*a\*b\* ΔE\*76 is used to check legend color separation and to match ML-segmented zones (ISO/CIE 11664-4)
- Optional ML segmentation for PNG via FastAPI (`ml/`)

## Build and Test

```bash
dotnet restore OpenRebar.sln --locked-mode -p:EnableWindowsTargeting=true -p:Platform=x64
dotnet build OpenRebar.sln --no-restore --configuration Release -p:EnableWindowsTargeting=true -p:Platform=x64
dotnet format OpenRebar.sln --verify-no-changes --no-restore
dotnet test tests/OpenRebar.Domain.Tests/OpenRebar.Domain.Tests.csproj --no-build --configuration Release -f net8.0
dotnet test tests/OpenRebar.Infrastructure.Tests/OpenRebar.Infrastructure.Tests.csproj --no-build --configuration Release -f net8.0
dotnet test tests/OpenRebar.TestCorpus/OpenRebar.TestCorpus.csproj --no-build --configuration Release -f net8.0
dotnet test tests/OpenRebar.Application.Tests/OpenRebar.Application.Tests.csproj --no-build --configuration Release -f net8.0
dotnet test tests/OpenRebar.Cli.Tests/OpenRebar.Cli.Tests.csproj --no-build --configuration Release -f net10.0
```

Current regression status (those five commands): **416/416 tests passing**.

## CI Quality Gates

The `build-and-test` workflow enforces claim-driven checks, not only compilation:

| Gate | Purpose | Failure effect |
|---|---|---|
| `dotnet restore --locked-mode` | lockfile integrity and deterministic dependency graph | blocks lane |
| `dotnet build` | compile-time integrity | blocks lane |
| `dotnet format --verify-no-changes --no-restore` | whitespace/style drift prevention | blocks lane |
| `dotnet test` + TRX | executable regression baseline | blocks lane |
| `verify_readme_regression_claim.py` | README EN/RU test-count claim parity against TRX | blocks lane |
| dependency governance reports | supply-chain visibility (`--vulnerable`, `--outdated`) | evidence artifacts |
| benchmark summary gate | threshold-based quality envelope evidence | blocks benchmark lane |

For audit-grade closure, use [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md).

## History

The April 2026 audits and execution plans are historical and are not the source of truth. They are in [docs/archive/](docs/archive/). The current plan is [docs/OPENREBAR_AGENT_PLAN_r7_2026_10_02.md](docs/OPENREBAR_AGENT_PLAN_r7_2026_10_02.md).

## Optional corpus rail

The normal test lane skips `BatchReinforcementCorpusFixtureTests` when no manifest is present. On push and pull request the corpus lane writes `corpus: NotEvaluated`. The scheduled and manually dispatched `benchmark-corpus` job sets `OPENREBAR_REQUIRE_BATCH_CORPUS=1` only when `OPENREBAR_BATCH_CORPUS_ROOT` is configured, and then fails if the manifest is missing. To run the envelope locally, add:

- `tests/OpenRebar.Application.Tests/Fixtures/BatchBenchmarkCorpus/manifest.json`, or
- set `OPENREBAR_BATCH_CORPUS_ROOT` to a directory containing the manifest and fixtures.

## Known limitations

Still open relative to [docs/OPENREBAR_AGENT_PLAN_2026_10_01.md](docs/OPENREBAR_AGENT_PLAN_2026_10_01.md):

- A single drawing is still one layer. A project file can name up to four drawings. Anchorage is drawn only as far as the working area allows. A hook is the centerline arc of the bend and has no straight tail. A bar longer than stock is lapped. Couplers are not modeled. Holes are counted rather than cut out of the zone.
- Column generation packs a single stock length. A dual bound is published only when `boundStatus` is `Proven`.
- The schedule is a CSV specification and a steel-mass sheet. XLSX is not written. IFC export has no `IfcReinforcingBarType` and no bar geometry.
- Raster input requires calibration. The optional ML service ships with an empty model manifest.
- A 50 mm grid rejects a layout when any cell is short of the specified area (`Passed` only at zero deficit). Development is measured from the placed end, so an edge without room for anchorage fails the check. Holes are still part of the checked area. `examples/dxf/simple-slab` stays failed because its edges are Free, the required area runs to the edge, and a hook is not credited. A free edge also insets the axis by cover + d/2 (40 mm for Ø20 and 30 mm cover): `edgeDevelopmentAreaM2` is 6.12, `realDeficitAreaM2` is 0.99, and `underReinforcedAreaM2` is 7.11.

## CLI Quickstart

```bash
dotnet run --project src/OpenRebar.Cli -- <isoline-file> [options]
```

Common options:

- `--legend <path>`: legend configuration (JSON)
- `--catalog <path>`: supplier catalog (JSON/CSV)
- `--ml-url <url>`: PNG segmentation service (for example `http://localhost:8101`)
- `--slab-width <mm>` / `--slab-height <mm>` / `--thickness <mm>` / `--cover <mm>`

The CLI writes the canonical report next to the input file (`.result.json`) and also emits `.schedule.csv`, `.aerobim.json`, and `.reinforcement.ifc` exports.

## Canonical Examples and Snapshots

The repository now ships canonical reproducible examples under `examples/`:

- `examples/dxf/simple-slab/input.dxf`
- `examples/png/simple-slab/input.png`
- `examples/fe-field/supported-slab.csv` with `supported-slab.project.json` (slab on walls, verification passed)

Each example has committed expected snapshots:

- `expected/input.result.json`
- `expected/input.schedule.csv`

Regeneration scripts:

```bash
# Linux/macOS
bash tools/examples/generate_expected_outputs.sh

# Windows PowerShell
powershell -ExecutionPolicy Bypass -File tools/examples/generate_expected_outputs.ps1
```

Snapshot verification is executed in `ExamplesSnapshotTests` (`tests/OpenRebar.Cli.Tests`).

## Python ML Module (Optional)

### Installation with Supply-Chain Security

The ML module uses cryptographically-verified dependencies to prevent supply-chain attacks:

```bash
cd ml

# Install with hash verification (recommended for production)
pip install --require-hashes -r requirements.locked.txt

# Or use the standard requirements (development)
pip install -r requirements.txt

# Run tests
pytest tests -q

# Start segmentation service
uvicorn src.api.server:app --port 8101
```

**Security**: See [ml/SUPPLY_CHAIN_SECURITY.md](ml/SUPPLY_CHAIN_SECURITY.md) for:
- Dependency lock file with SHA256 hashes
- Model checkpoint manifest at `ml/models/MANIFEST.json` with integrity verification
- CI/CD verification procedures
- Weekly dependency re-pinning workflow

CI executes the Python smoke tests with an explicit `PYTHONPATH` pointing to `ml/` and installs dependencies from `ml/requirements.locked.txt` using `--require-hashes`.

## Revit Host Boundary

The Revit host is compiled under `#if REVIT_SDK` and requires local Autodesk Revit references. Copy `addin/RevitNet8` next to the `net8.0-windows` build when the Revit process is still .NET 8, and `addin/RevitNet10` next to the `net10.0-windows` build when it is .NET 10. See [RELEASE_POLICY.md](RELEASE_POLICY.md).
The repository includes:

- DI composition root (`src/OpenRebar.RevitPlugin/Bootstrap.cs`)
- placement implementation using `Rebar.CreateFromCurves`
- tag creation pass (`IndependentTag.Create`)
- bending-shape tracking (element creation is intentionally left as an explicit TODO boundary)
- bar axes at cover + d/2, with the inner direction of a face on top of the outer one; top cover is read from `CLEAR_COVER_TOP`
- safe mode: a bar with a hook, L, or U end is not placed as a straight line. It is reported as an error, and the schedule and IFC stay the source of truth

## Project Docs

- Documentation router: [docs/README.md](docs/README.md)
- **Normative Traceability**: [docs/NORMATIVE_TRACEABILITY.md](docs/NORMATIVE_TRACEABILITY.md) — mapping of SP 63 clauses to code and tests
- Architecture notes: [docs/architecture.md](docs/architecture.md)
- Current plan: [docs/OPENREBAR_AGENT_PLAN_r9_2026_10_02.md](docs/OPENREBAR_AGENT_PLAN_r9_2026_10_02.md) (on top of r8 and r7)
- Changelog: [CHANGELOG.md](CHANGELOG.md)
- Contributing: [CONTRIBUTING.md](CONTRIBUTING.md)
- Security policy: [SECURITY.md](SECURITY.md)
- Code of conduct: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- Citation metadata: [CITATION.cff](CITATION.cff)
- Funding metadata: [.github/FUNDING.yml](.github/FUNDING.yml)

## Documentation Governance

To keep public claims and internal evidence aligned:

1. Update canonical surfaces first (`README.md`, `README.ru.md`, [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md), [docs/README.md](docs/README.md)).
2. Keep historical numbers dated in roadmap/audit docs; keep only current numbers in active claim surfaces.
3. If behavior changed, update command examples and CI-parity guidance in the same change.
4. Treat report snapshots as dated evidence, not evergreen truth.

## Scientific Reporting Standard

### Claim Boundary

This README distinguishes implemented and verified behavior from roadmap intent.

- Verified statements map to executable code paths, contracts, tests, and persisted reports.
- Planned work remains explicitly framed as future roadmap.
- Performance and quality metrics are tied to named artifacts and validation lanes, not universal guarantees.

### Reproducibility Baseline

Use [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md) as the canonical validation baseline before publishing engineering or benchmark claims.

Minimum repository baseline:

```bash
dotnet build OpenRebar.sln --configuration Release -p:EnableWindowsTargeting=true
dotnet test tests/OpenRebar.Domain.Tests/OpenRebar.Domain.Tests.csproj --configuration Release -f net8.0
dotnet test tests/OpenRebar.Infrastructure.Tests/OpenRebar.Infrastructure.Tests.csproj --configuration Release -f net8.0
dotnet test tests/OpenRebar.TestCorpus/OpenRebar.TestCorpus.csproj --configuration Release -f net8.0
dotnet test tests/OpenRebar.Application.Tests/OpenRebar.Application.Tests.csproj --configuration Release -f net8.0
dotnet test tests/OpenRebar.Cli.Tests/OpenRebar.Cli.Tests.csproj --configuration Release -f net10.0
cd ml
python -m pip install --require-hashes -r requirements.locked.txt
python -m pytest tests -q
```

If you changed ML dependency governance or workflow behavior, also verify the pinned lock-refresh path documented in [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md):

```bash
cd ml
python -m pip install --require-hashes -r ..\.github\requirements\pip-tools.locked.txt
python -m piptools compile --allow-unsafe --generate-hashes --output-file=requirements.locked.txt requirements.in
```

For report-derived claims, include the report file path, schema contract version, and commit SHA.

### Citation and Research Reuse

- Cite software metadata via [CITATION.cff](CITATION.cff).
- For comparative studies, report both achieved quality indicators and unresolved boundary conditions.
- Keep normative profile and tables version visible when sharing external evidence.

## Governance

- [Support](SUPPORT.md)
- [Contributing](CONTRIBUTING.md)
- [Security Policy](SECURITY.md)
- [Maintainers](MAINTAINERS.md)
- [Release Policy](RELEASE_POLICY.md)
- [Citation Metadata](CITATION.cff)

## License

MIT — see [LICENSE](LICENSE).
