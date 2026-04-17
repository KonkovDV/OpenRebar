# OpenRebar — Reinforcement Automation for RC Slabs

[![CI](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml/badge.svg)](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

English | [Русский](README.ru.md)

OpenRebar is a .NET 8 codebase for generating reinforcement layouts for flat RC slabs from isoline-based inputs (DXF or PNG). The repository is designed to keep the **engineering logic testable outside Revit** while still supporting a Revit 2025 plugin boundary.

This repo ships three execution surfaces:

- **CLI host** (`src/OpenRebar.Cli`) — batch runs, CI, debugging, integration artifacts
- **Revit host** (`src/OpenRebar.RevitPlugin`) — external command + UI (compiled only when Revit SDK references are available)
- **Optional ML module** (`ml/`) — U-Net segmentation for PNG isolines exposed via HTTP (FastAPI)

## Problem Statement

The motivating workflow is reinforcement placement in Revit from isoline maps exported by tools such as LIRA-SAPR / Stark-ES. Manual placement is time-consuming and susceptible to inconsistency across floors, zones, and engineers.

OpenRebar implements a reproducible pipeline:

1. Parse an isoline file (**DXF** or **PNG**) into color-coded reinforcement zones
2. Classify zones and decompose complex polygons into rectangles (with persisted coverage/over-coverage evidence)
3. Calculate reinforcement layout per zone (spacing, diameter, anchorage rules)
4. Optimize cutting (1D CSP) to reduce waste (column-generation-style baseline + exact small-instance fallback)
5. Persist auditable machine-readable artifacts for downstream BIM systems
6. (When enabled) place shape-driven Rebar elements in Revit and generate tags / bending-shape tracking

The target is to reduce routine slab reinforcement placement from engineer-weeks to engineer-hours **under validated assumptions** and with a stable, reviewable output contract.

## Outputs and Integration Contract

The repository treats the machine-readable outputs as first-class artifacts.

| Artifact | Produced by | Purpose |
|---|---|---|
| `*.result.json` | Pipeline / CLI | Canonical reinforcement execution report (schema-backed) |
| `*.aerobim.json` | CLI exporter | AeroBIM-oriented summary export |
| `*.schedule.csv` | CLI exporter | Rebar schedule export for downstream spreadsheets/ERP |
| `*.reinforcement.ifc` | CLI exporter | IFC export surface (IFC4 via xBIM) |

**Canonical schema:** `contracts/aerobim-reinforcement-report.schema.json`

The canonical report explicitly persists:

- `normativeProfile` (for example `ru.sp63.2018`) and a versioned table-set id (for example `ru.sp63.2018.tables.v1`)
- `analysisProvenance` for geometry decomposition and cutting optimisation (algorithm ids, thresholds, and fallbacks)

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
| `ColumnGenerationOptimizer` | Production-oriented baseline with persisted optimisation provenance and exact small-instance fallback |
| `FirstFitDecreasingOptimizer` | Heuristic baseline and fallback |

The column-generation implementation should be understood as a strong engineering baseline (LP/pricing/repair), not as a mathematically complete branch-and-price solver. This distinction is persisted into the canonical report provenance.

### DXF/PNG Color Recognition

- **DXF:** AutoCAD ACI palette (256 colors) + ByLayer resolution
- **PNG:** CIE L\*a\*b\* ΔE\*76 color matching (ISO/CIE 11664-4)
- Optional ML segmentation for PNG via FastAPI (`ml/`)

## Build and Test

```bash
dotnet build OpenRebar.sln
dotnet test OpenRebar.sln
```

Current regression status (local `dotnet test OpenRebar.sln`): **160/160 tests passing**.

### Optional corpus rail

The manifest-driven corpus rail is intentionally optional. To enable it, add:

- `tests/OpenRebar.Application.Tests/Fixtures/BatchBenchmarkCorpus/manifest.json`, or
- set `OPENREBAR_BATCH_CORPUS_ROOT` to a directory containing the manifest and fixtures.

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

## Python ML Module (Optional)

```bash
cd ml
pip install -r requirements.txt
pytest tests -q
uvicorn src.api.server:app --port 8101
```

CI executes the Python smoke tests with an explicit `PYTHONPATH` pointing to `ml/`.

## Revit Host Boundary

The Revit host is compiled under `#if REVIT_SDK` and requires local Autodesk Revit references.
The repository includes:

- DI composition root (`src/OpenRebar.RevitPlugin/Bootstrap.cs`)
- placement implementation using `Rebar.CreateFromCurves`
- tag creation pass (`IndependentTag.Create`)
- bending-shape tracking (element creation is intentionally left as an explicit TODO boundary)

## Project Docs

- Architecture notes: [docs/architecture.md](docs/architecture.md)
- Changelog: [CHANGELOG.md](CHANGELOG.md)
- Audit and roadmap: [HYPER_DEEP_AUDIT_REPORT.md](HYPER_DEEP_AUDIT_REPORT.md), [TASKS.md](TASKS.md)
- Contributing: [CONTRIBUTING.md](CONTRIBUTING.md)
- Security policy: [SECURITY.md](SECURITY.md)
- Code of conduct: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)

## License

MIT — see [LICENSE](LICENSE).
