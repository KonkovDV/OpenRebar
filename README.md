# A101-Reinforcement

Automated reinforcement placement for flat RC slabs — Revit 2025 plugin with ML-powered isoline parsing.

## Problem

Structural engineers at A101 manually place reinforcement in Revit based on isoline maps exported from LIRA-SAPR. For a typical 25-floor residential building this takes **2-3 weeks per floor** and is highly error-prone.

This plugin automates the full pipeline:
1. Parse isoline file (DXF/PNG) → extract color-coded reinforcement zones
2. Classify zones and decompose complex polygons into rectangles
3. Calculate rebar layout per zone (spacing, diameter, anchorage per SP 63.13330)
4. Optimize cutting to minimize waste (bin-packing / cutting stock problem)
5. Place `RebarInSystem` elements in Revit with tags and bending details

Target: reduce reinforcement placement from **2-3 weeks → 2-3 hours** per floor.

## Architecture

Clean Architecture with 4 layers:

```
┌──────────────────────────────────────────────┐
│              A101.RevitPlugin                 │  ← Revit ExternalCommand + WPF UI
│  Bootstrap.cs (DI), Commands/, UI/           │
├──────────────────────────────────────────────┤
│            A101.Application                   │  ← Use cases / orchestration
│  GenerateReinforcementPipeline               │
│  OptimizeRebarCuttingUseCase                 │
├──────────────────────────────────────────────┤
│             A101.Domain                       │  ← Models, ports (interfaces), rules
│  Models/  Ports/  Rules/                     │
│  (zero dependencies — pure C#)               │
├──────────────────────────────────────────────┤
│          A101.Infrastructure                  │  ← Adapters (concrete implementations)
│  DxfIsolineParser, PngIsolineParser          │
│  FirstFitDecreasingOptimizer                 │
│  StandardReinforcementCalculator             │
│  StandardZoneDetector                        │
│  FileSupplierCatalogLoader                   │
├──────────────────────────────────────────────┤
│              ml/ (Python)                     │  ← U-Net segmentation for PNG isolines
│  FastAPI server at :8101                     │
└──────────────────────────────────────────────┘
```

**Dependency rule**: Domain → nothing. Application → Domain. Infrastructure → Domain + Application. Plugin → all.

## Domain Ports (Interfaces)

| Port | Purpose |
|------|---------|
| `IIsolineParser` | Parse DXF/PNG into reinforcement zones |
| `IZoneDetector` | Classify zones, decompose complex polygons |
| `IReinforcementCalculator` | Generate rebar segments per zone |
| `IRebarOptimizer` | Bin-packing optimization for cutting |
| `ISupplierCatalogLoader` | Load available stock lengths |
| `IRevitPlacer` | Place rebars in Revit model |
| `IImageSegmentationService` | ML-based image segmentation (Python) |

## Normative Base

- **SP 63.13330.2018** — Concrete and reinforced concrete structures (Russian code)
- **GOST 5781-82** — Rebar dimensions and mass
- Anchorage lengths: `l_an = Rs·d / (4·Rbt)`
- Lap splices: `l_overlap = 1.2 · l_an`
- Min reinforcement ratio: `μ_min = 0.1%`
- Max spacing: `min(1.5h, 400mm)` primary, `min(3.5h, 500mm)` secondary

## Prerequisites

- .NET 8 SDK
- Revit 2025 (for plugin execution — not needed for development/tests)
- Python 3.11+ (for ML module)

## Build & Test

```bash
# Restore + build
dotnet build A101.sln

# Run tests
dotnet test A101.sln

# Python ML setup
cd ml
pip install -r requirements.txt
uvicorn src.api.server:app --port 8101
```

## Project Structure

```
A101.sln
├── src/
│   ├── A101.Domain/           # Models, Ports, Rules (pure, zero deps)
│   ├── A101.Application/      # Use cases (depends on Domain)
│   ├── A101.Infrastructure/   # Adapters: DXF, PNG, optimizer, calculator
│   └── A101.RevitPlugin/      # Revit entry point, DI bootstrap, WPF UI
├── tests/
│   ├── A101.Domain.Tests/
│   ├── A101.Application.Tests/
│   └── A101.Infrastructure.Tests/
├── ml/
│   ├── src/
│   │   ├── segmentation/      # U-Net model + inference
│   │   └── api/               # FastAPI server
│   ├── models/                # Trained model weights
│   └── requirements.txt
└── docs/
    └── architecture.md
```

## Key Algorithms

### Rebar Cutting Optimization (Bin-Packing)
First Fit Decreasing (FFD) — sorts required lengths descending, packs each into the first bin with space. Achieves ~85-90% stock utilization vs. ~70% manual.

### Polygon Decomposition
Grid-based decomposition: subdivide bounding box into cells, keep cells inside polygon, merge adjacent cells into rectangles. Handles L-shaped zones, zones around openings.

### Image Segmentation (ML)
Lightweight U-Net (3→32→64→128→256→512 channels) trained on LIRA-SAPR isoline exports. Outputs per-pixel class mask → contours → simplified polygons via Douglas-Peucker.

## License

Proprietary — А101 Group.
