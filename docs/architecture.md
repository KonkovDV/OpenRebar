# Architecture

## Overview

A101-Reinforcement follows Clean Architecture with strict dependency inversion. The core insight: **domain logic (SP 63 rules, optimization algorithms) must be testable without Revit or any I/O**.

## Layer Dependency Graph

```
Domain (pure) ← Application (orchestration) ← Infrastructure (adapters) ← RevitPlugin (composition root)
```

- **Domain**: Zero external dependencies. Contains models, ports (interfaces), and business rules.
- **Application**: Orchestrates use cases by composing domain ports. Depends only on Domain.
- **Infrastructure**: Implements adapters for each port. Depends on Domain + Application.
- **RevitPlugin**: DI composition root. Wires everything together. Depends on all layers.

## Design Decisions

### 1. Domain Ports as Interfaces

Every I/O or external dependency is abstracted behind a domain port (interface):

```csharp
// Domain port — pure contract
public interface IIsolineParser
{
    Task<IReadOnlyList<ReinforcementZone>> ParseAsync(
        string filePath, ColorLegend legend, CancellationToken ct);
}

// Infrastructure adapter — concrete implementation
public class DxfIsolineParser : IIsolineParser { ... }
public class PngIsolineParser : IIsolineParser { ... }
```

This allows:
- Testing domain logic with mocks
- Swapping DXF parser for a different implementation
- Running the full pipeline outside Revit

### 2. Separate ML Module (Python)

Image segmentation uses PyTorch U-Net which is impractical to host in .NET. The Python service runs as a standalone FastAPI server on port 8101. The C# `PngIsolineParser` can operate in two modes:
1. **With ML**: Calls the Python service for neural segmentation
2. **Without ML**: Falls back to color quantization + connected components (less accurate but works offline)

### 3. Revit Placer as Domain Port

`IRevitPlacer` is defined in Domain as a pure interface. The real Revit implementation (`RevitRebarPlacer`) lives in the plugin project and is only compiled when the Revit SDK NuGet is available. A `StubRevitPlacer` enables full pipeline testing without Revit.

### 4. Optimization as Bin-Packing

Rebar cutting is a **1D Cutting Stock Problem** (CSP). We use First Fit Decreasing (FFD) which is a known O(n log n) heuristic achieving ≤ 11/9·OPT + 6/9 bins. Future improvement: column generation LP for exact optimal.

### 5. Supplier Catalogs

Stock lengths vary by supplier and market. The `ISupplierCatalogLoader` port enables:
- JSON catalogs from supplier APIs
- CSV imports from procurement systems
- Default Russian market lengths (6m, 9m, 11.7m, 12m)

## Normative Rules Engine

All SP 63 / GOST calculations are in `A101.Domain.Rules/`:

| Module | Responsibility |
|--------|---------------|
| `AnchorageRules` | Anchorage length (l_an), lap splices, bond stress lookup |
| `ReinforcementLimits` | Min/max reinforcement ratio, spacing limits per SP 63 |
| `PolygonDecomposition` | Zone geometry → axis-aligned rectangles |

These are pure static functions with no I/O — fully testable with unit tests.

## Data Flow

```
                            ┌─────────────┐
                            │ LIRA-SAPR   │
                            │ isoline.dxf │
                            └──────┬──────┘
                                   │
                         ┌─────────▼──────────┐
                         │  IIsolineParser     │
                         │  (DXF or PNG+ML)    │
                         └─────────┬──────────┘
                                   │  List<ReinforcementZone>
                         ┌─────────▼──────────┐
                         │  IZoneDetector      │
                         │  classify + decomp  │
                         └─────────┬──────────┘
                                   │  Classified zones
                         ┌─────────▼──────────┐
                         │  IReinforcement-    │
                         │  Calculator         │
                         └─────────┬──────────┘
                                   │  Zones with RebarSegments
                    ┌──────────────┼──────────────┐
                    │              │               │
          ┌─────────▼──────┐      │    ┌──────────▼──────────┐
          │ IRebarOptimizer │      │    │   IRevitPlacer      │
          │ cutting plans   │      │    │   place in model    │
          └────────────────┘      │    └─────────────────────┘
                                  │
                         ┌────────▼───────────┐
                         │  Pipeline Result   │
                         │  zones, waste %,   │
                         │  mass, cost        │
                         └────────────────────┘
```

## Testing Strategy

| Layer | Test Type | Framework |
|-------|-----------|-----------|
| Domain.Rules | Unit tests | xUnit + FluentAssertions |
| Domain.Models | Unit tests | xUnit + FluentAssertions |
| Application | Integration (mocked ports) | xUnit + NSubstitute |
| Infrastructure | Integration | xUnit + FluentAssertions |
| RevitPlugin | Manual (requires Revit) | — |
| ML | Unit + integration | pytest |

## Future Roadmap

1. **Column generation LP** for exact cutting optimization
2. **Revit view filters** for color-coded zone visualization
3. **Multi-slab batch processing** across floors
4. **Export to IFC** for BIM collaboration
5. **Training pipeline** for U-Net on customer-specific isoline styles
