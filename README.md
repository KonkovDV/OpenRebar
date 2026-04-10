# A101-Reinforcement

[![CI](https://github.com/user/a101-reinforcement/actions/workflows/ci.yml/badge.svg)](https://github.com/user/a101-reinforcement/actions/workflows/ci.yml)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Automated reinforcement placement for flat RC slabs — **Revit 2025 plugin** with ML-powered isoline parsing.

## Problem

Structural engineers at A101 manually place reinforcement in Revit based on isoline maps exported from LIRA-SAPR / Stark-ES. For a typical 25-floor residential building this takes **2–3 weeks per floor** and is error-prone.

This plugin automates the full pipeline:

1. **Parse** isoline file (DXF / PNG) → extract color-coded reinforcement zones
2. **Classify** zones and decompose complex polygons into rectangles
3. **Calculate** rebar layout per zone (spacing, diameter, anchorage per SP 63.13330)
4. **Optimize** cutting to minimise waste (Column Generation / bin-packing)
5. **Place** `RebarInSystem` elements in Revit with tags and bending details

**Target:** reduce reinforcement placement from **2–3 weeks → 2–3 hours** per floor.

## Architecture

Clean Architecture with 4 layers:

```
┌──────────────────────────────────────────────┐
│            A101.RevitPlugin                   │  Revit ExternalCommand + WPF UI
│  Bootstrap (DI), Commands/, Revit/           │
├──────────────────────────────────────────────┤
│           A101.Application                    │  Use cases / orchestration
│  GenerateReinforcementPipeline               │
│  OptimizeRebarCuttingUseCase                 │
├──────────────────────────────────────────────┤
│            A101.Domain                        │  Models, Ports, Rules (zero deps)
│  Geometry  Isoline  ReinforcementZone        │
│  AnchorageRules  ReinforcementLimits         │
├──────────────────────────────────────────────┤
│         A101.Infrastructure                   │  Adapters
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

### Cutting Optimisation

Two optimiser implementations behind the `IRebarOptimizer` port:

| Algorithm | Waste | Speed | Best for |
|-----------|-------|-------|----------|
| **Column Generation** (Gilmore–Gomory 1961) | ≤ 5–8% | O(I·S·C) per CG iter | Large jobs, mixed diameters |
| **First Fit Decreasing** (FFD) | 10–15% | O(n log n) | Quick estimates, small batches |

Column Generation solves the LP relaxation of the 1D cutting stock problem, then rounds via largest-remainder + greedy repair.

### Color Recognition

- **DXF:** Full AutoCAD ACI palette (256 colors) + ByLayer resolution
- **PNG:** CIE L\*a\*b\* ΔE\*76 colour matching (ISO/CIE 11664-4)
- Isoline legend → `ColorLegend` with parametric `maxDeltaE` threshold

### Layout Engine

- Top / Bottom rebar layers with correct bond condition (η₂)
- X + Y direction detection from zone aspect ratio
- Per-zone mark numbering for rebar schedules
- Polygon decomposition for L-shaped / around-opening zones

## Domain Ports

| Port | Purpose |
|------|---------|
| `IIsolineParser` | Parse DXF/PNG into reinforcement zones |
| `IZoneDetector` | Classify zones, decompose complex polygons |
| `IReinforcementCalculator` | Generate rebar segments per zone |
| `IRebarOptimizer` | Cutting stock optimisation |
| `ISupplierCatalogLoader` | Load available stock lengths + prices |
| `IRevitPlacer` | Place rebars in Revit model |
| `IImageSegmentationService` | ML-based image segmentation (Python) |

## Prerequisites

- .NET 8 SDK
- Revit 2025 (for plugin execution — not needed for development/tests)
- Python 3.11+ (for ML module, optional)

## Build & Test

```bash
dotnet build A101.sln
dotnet test A101.sln

# Python ML setup (optional)
cd ml
pip install -r requirements.txt
uvicorn src.api.server:app --port 8101
```

## Project Structure

```
A101.sln
├── src/
│   ├── A101.Domain/           # Models, Ports, Rules (pure C#, zero deps)
│   ├── A101.Application/      # Use cases (depends on Domain)
│   ├── A101.Infrastructure/   # Adapters: DXF, PNG, optimisers, calculator
│   └── A101.RevitPlugin/      # Revit entry point, DI bootstrap, WPF UI
├── tests/
│   ├── A101.Domain.Tests/     # Anchorage, polygon, colour, validation
│   ├── A101.Application.Tests/
│   └── A101.Infrastructure.Tests/  # Optimisers, calc engine, zone detector
├── ml/
│   ├── src/segmentation/      # U-Net model + inference
│   ├── src/api/               # FastAPI server
│   └── requirements.txt
├── .github/workflows/ci.yml   # GitHub Actions CI
└── LICENSE
```

## License

MIT — see [LICENSE](LICENSE).

### Image Segmentation (ML)
Lightweight U-Net (3→32→64→128→256→512 channels) trained on LIRA-SAPR isoline exports. Outputs per-pixel class mask → contours → simplified polygons via Douglas-Peucker.

## License

Proprietary — А101 Group.
