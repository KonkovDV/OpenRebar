# OpenRebar-Reinforcement

[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Automated reinforcement placement for flat RC slabs — **Revit 2025 plugin** with ML-powered isoline parsing.

## Status

- This repository is a **standalone GitHub project**, extracted from MicroPhoenix architectural patterns and now maintained independently.
- The core domain, application, optimisation, DXF, PNG, and ML service layers are implemented and testable outside Revit.
- The Revit SDK-dependent command and placer are included as **compile-time scaffolds** and require local Autodesk Revit references to enable the real plugin boundary.

## Problem

Structural engineers at OpenRebar manually place reinforcement in Revit based on isoline maps exported from LIRA-SAPR / Stark-ES. For a typical 25-floor residential building this takes **2–3 weeks per floor** and is error-prone.

This plugin automates the full pipeline:

1. **Parse** isoline file (DXF / PNG) → extract color-coded reinforcement zones
2. **Classify** zones and decompose complex polygons into rectangles
3. **Calculate** rebar layout per zone (spacing, diameter, anchorage per SP 63.13330)
4. **Optimize** cutting to minimise waste (column-generation-style CSP with exact small-instance search for tiny batches and benchmark-pack TEVV)
5. **Persist** a canonical machine-readable reinforcement report for downstream BIM systems, including normative profile and analysis provenance
6. **Place** `RebarInSystem` elements in Revit with tags and bending details

**Target:** reduce reinforcement placement from **2–3 weeks → 2–3 hours** per floor.

## Integration Contract

- Canonical report schema: `contracts/aerobim-reinforcement-report.schema.json`
- Canonical report artifact: `*.result.json`
- Primary downstream target: AeroBIM and adjacent BIM/data consumers that need stable slab, zone, cutting, and placement summaries

The standalone project now emits a formal JSON contract instead of relying only on an ad-hoc CLI export shape. The canonical report now carries a first-class normative profile (`ru.sp63.2018`) and analysis provenance for geometry decomposition and optimization, which makes downstream review and audit materially easier.

The SP 63 normative tables are now loaded from a versioned embedded resource (`ru.sp63.2018.tables.v1`) instead of being duplicated across `switch` blocks. That makes the normative basis explicit, testable, and easier to revise when additional profile sets are introduced.

## Architecture

Clean Architecture with 4 layers:

```
┌──────────────────────────────────────────────┐
│            OpenRebar.RevitPlugin                   │  Revit ExternalCommand + WPF UI
│  Bootstrap (DI), Commands/, Revit/           │
├──────────────────────────────────────────────┤
│           OpenRebar.Application                    │  Use cases / orchestration
│  GenerateReinforcementPipeline               │
│  OptimizeRebarCuttingUseCase                 │
├──────────────────────────────────────────────┤
│            OpenRebar.Domain                        │  Models, Ports, Rules (zero deps)
│  Geometry  Isoline  ReinforcementZone        │
│  AnchorageRules  ReinforcementLimits         │
├──────────────────────────────────────────────┤
│         OpenRebar.Infrastructure                   │  Adapters
│  DxfIsolineParser  PngIsolineParser          │
│  ColumnGeneration / FFD Optimizers           │
│  StandardReinforcementCalculator             │
│  StandardZoneDetector                        │
│  FileSupplierCatalogLoader                   │
├──────────────────────────────────────────────┤
│             ml/ (Python)                      │  U-Net segmentation for PNG isolines
│  FastAPI server :8101                        │
└──────────────────────────────────────────────┘
```

**Dependency rule:** Domain → nothing. Application → Domain. Infrastructure → Domain + Application. Plugin → all.

## Key Features

### Normative Engine (SP 63.13330.2018)

| Rule | Formula | Source |
|------|---------|--------|
| Anchorage | l₀,an = R_s · d / (4 · η₁ · η₂ · R_bt) | SP 63 §10.3.24 |
| Bond coefficients | η₁ = 2.5 (ribbed) / 1.5 (smooth); η₂ = 1.0 (good) / 0.7 (poor) | SP 63 §10.3.24 |
| Lap splice | l_lap = α · l₀,an; α ∈ {1.2, 1.4, 2.0} per lap percentage | SP 63 §10.3.31 |
| Max spacing | min(1.5h, 400mm) primary; min(3.5h, 500mm) secondary | SP 63 §10.3.8 |
| Min reinforcement | μ_min = 0.1% | SP 63 §10.3.5 |

The current SP 63 implementation is backed by a versioned embedded profile resource and golden tests for bond stress, design strength, periodic-profile classification, and linear mass tables.

### Cutting Optimisation

Two optimiser implementations behind the `IRebarOptimizer` port:

| Algorithm | Waste | Speed | Best for |
|-----------|-------|-------|----------|
| **ColumnGenerationOptimizer** | Lower waste on mixed batches than FFD in current implementation | Higher than FFD | Default production-oriented baseline with exact small-instance path and persisted LP/pricing provenance |
| **First Fit Decreasing** (FFD) | Simpler heuristic baseline | O(n log n) | Quick estimates, fallback |

The current default optimizer uses column-generation-style pattern search and heuristic repair. It is structured so the repository can later swap in a true LP master / branch-and-price backend without changing domain or application contracts.

This matters for technical due diligence: the current implementation is stronger than a simple heuristic baseline, but it should still be presented honestly as a production-oriented optimizer rather than a mathematically complete branch-and-price engine. The repository now makes that distinction explicit in both code comments and persisted optimizer provenance. For tiny instances, the optimizer now falls back to an exact discrete search instead of pretending heuristic quality is enough.

### Color Recognition

- **DXF:** Full AutoCAD ACI palette (256 colors) + ByLayer resolution
- **PNG:** CIE L\*a\*b\* ΔE\*76 colour matching (ISO/CIE 11664-4)
- Isoline legend → `ColorLegend` with parametric `maxDeltaE` threshold

### Layout Engine

- Top / Bottom rebar layers with correct bond condition (η₂)
- X + Y direction detection from zone aspect ratio
- Per-zone mark numbering for rebar schedules
- Polygon decomposition for L-shaped / around-opening zones with an exact orthogonal-strip path where possible and persisted coverage/over-coverage metrics

### Observability

- Entry points use `ILogger<T>` from .NET DI rather than handwritten logging helpers
- Calculator warnings (for example spacing above code maxima) are emitted through structured templates
- Pipeline execution is logged with stable scope fields such as `projectCode` and `slabId`

## Domain Ports

| Port | Purpose |
|------|---------|
| `IIsolineParser` | Parse DXF/PNG into reinforcement zones |
| `IZoneDetector` | Classify zones, decompose complex polygons |
| `IReinforcementCalculator` | Generate rebar segments per zone |
| `IRebarOptimizer` | Cutting stock optimisation |
| `ISupplierCatalogLoader` | Load available stock lengths + prices |
| `IReportStore` | Persist canonical reinforcement execution reports |
| `IRevitPlacer` | Place rebars in Revit model |
| `IImageSegmentationService` | ML-based image segmentation (Python) |

## Verification-Oriented Reporting

The canonical report (`*.result.json`) now persists:

- normative profile identity and table-set version
- geometry decomposition provenance (algorithm, thresholds, sampling resolution)
- optimization provenance (optimizer id, LP/pricing strategy, integerization, fallback usage)
- per-zone decomposition coverage and over-coverage metrics for complex zones

Current validation rails include exact small-instance CSP checks, a benchmark pack covering score-gap and waste-gap distribution, a real-adapter batch benchmark pack that drives generated DXF slabs through the end-to-end application pipeline, and an optional fixture-driven corpus rail for production slab batches.

Production slab-batch cutting benchmarks are still pending fixture acquisition: the repository now ships both a generated DXF harness and a manifest-driven corpus-ready rail under `tests/OpenRebar.Application.Tests/Fixtures/BatchBenchmarkCorpus`, but not production slab-batch datasets or canonical `*.result.json` benchmark corpora.

Current full .NET regression status: `158/158` green.

## Prerequisites

- .NET 8 SDK
- Revit 2025 (for plugin execution — not needed for development/tests)
- Python 3.11+ (for ML module, optional)

## Build & Test

```bash
dotnet build OpenRebar.sln
dotnet test OpenRebar.sln
dotnet test tests/OpenRebar.Application.Tests/OpenRebar.Application.Tests.csproj --filter Category=Corpus

# Python ML setup / smoke tests
cd ml
pip install -r requirements.txt
pytest tests -q
uvicorn src.api.server:app --port 8101
```

The Python smoke rail now covers not only inference/server behavior, but also the training dataset loader, a one-epoch CPU training/evaluation pass on synthetic samples, and ONNX export.

If you publish this repository to GitHub, add the repository-specific CI badge URL after the final owner/repo name is known.

## Community And Security

- Security policy: [SECURITY.md](SECURITY.md)
- Contribution guide: [CONTRIBUTING.md](CONTRIBUTING.md)
- Code of conduct: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- Architecture notes: [docs/architecture.md](docs/architecture.md)
- Changelog: [CHANGELOG.md](CHANGELOG.md)
- Audit and roadmap: [HYPER_DEEP_AUDIT_REPORT.md](HYPER_DEEP_AUDIT_REPORT.md), [TASKS.md](TASKS.md)

For a public GitHub launch, this repository now includes a baseline CI workflow,
CodeQL workflow, dependency review workflow, Dependabot configuration, issue forms,
and CODEOWNERS wiring. GitHub-side controls such as private vulnerability reporting,
secret scanning, push protection, and branch rulesets still need to be enabled in
repository settings after the first remote push.

## Project Structure

```
OpenRebar.sln
├── src/
│   ├── OpenRebar.Domain/           # Models, Ports, Rules (pure C#, zero deps)
│   ├── OpenRebar.Application/      # Use cases (depends on Domain)
│   ├── OpenRebar.Infrastructure/   # Adapters: DXF, PNG, optimisers, calculator
│   └── OpenRebar.RevitPlugin/      # Revit entry point, DI bootstrap, WPF UI
├── tests/
│   ├── OpenRebar.Domain.Tests/     # Anchorage, polygon, colour, validation
│   ├── OpenRebar.Application.Tests/
│   └── OpenRebar.Infrastructure.Tests/  # Optimisers, calc engine, zone detector
├── ml/
│   ├── src/segmentation/      # U-Net model + inference
│   ├── src/api/               # FastAPI server
│   └── requirements.txt
├── contracts/                 # JSON Schema for canonical report
├── .github/workflows/
│   ├── ci.yml                 # CI with SBOM generation
│   ├── codeql.yml             # CodeQL security scanning
│   ├── dependency-review.yml  # PR dependency review
│   └── release.yml            # Tag-triggered release with attestation
├── CHANGELOG.md               # Keep a Changelog format
└── LICENSE
```

## License

MIT — see [LICENSE](LICENSE).
