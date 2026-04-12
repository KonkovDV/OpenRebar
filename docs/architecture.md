# Architecture

## Overview

OpenRebar-Reinforcement follows Clean Architecture with strict dependency inversion. The core insight: **domain logic (SP 63 rules, geometry processing, optimization algorithms) must be testable without Revit or any I/O**.

This repository is a standalone project built from an extraction of proven MicroPhoenix architectural patterns:

- domain-owned ports and business rules
- application-layer orchestration without framework leakage
- infrastructure adapters behind stable contracts
- composition root at the outermost executable boundary
- external AI/ML runtime kept out of the domain and .NET core

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

Image segmentation uses PyTorch U-Net which is impractical to host in .NET. The Python service runs as a standalone FastAPI server on port 8101. It now uses FastAPI lifespan-based startup/shutdown wiring, matching current official guidance. The C# `PngIsolineParser` can operate in two modes:
1. **With ML**: Calls the Python service for neural segmentation
2. **Without ML**: Falls back to color quantization + connected components (less accurate but works offline)

### 3. Revit Placer as Domain Port

`IRevitPlacer` is defined in Domain as a pure interface. The real Revit implementation (`RevitRebarPlacer`) lives in the plugin project and is only compiled when the Revit SDK NuGet is available. A `StubRevitPlacer` enables full pipeline testing without Revit.

### 4. Optimization as Bin-Packing / Cutting Stock

Rebar cutting is a **1D Cutting Stock Problem** (CSP). The repository now contains two optimizers behind `IRebarOptimizer`:

1. **ColumnGenerationOptimizer** as the default implementation for production-oriented runs
2. **FirstFitDecreasingOptimizer** as a simple heuristic fallback and baseline

This keeps the domain independent from any specific OR solver while allowing the standalone project to evolve toward exact or branch-and-price implementations later.

Important implementation note: the current `ColumnGenerationOptimizer` is an LP-relaxation / pricing / rounding pipeline with an FFD non-regression floor. It should be presented as a strong production-oriented optimizer, not as a mathematically complete branch-and-price solver. The canonical report now persists optimizer provenance (LP strategy, pricing strategy, integerization, fallback usage) so that this distinction is machine-readable.

For tiny instances, the optimizer now uses an exact discrete search path instead of forcing every case through the CG approximation stack. This keeps the production baseline honest while improving correctness on small mixed-stock batches.

### 5. Supplier Catalogs

Stock lengths vary by supplier and market. The `ISupplierCatalogLoader` port enables:
- JSON catalogs from supplier APIs
- CSV imports from procurement systems
- Default Russian market lengths (6m, 9m, 11.7m, 12m)

## Normative Rules Engine

All SP 63 / GOST calculations are in `OpenRebar.Domain.Rules/`:

| Module | Responsibility |
|--------|---------------|
| `AnchorageRules` | Anchorage length (l_an), lap splices, bond stress lookup |
| `ReinforcementLimits` | Min/max reinforcement ratio, spacing limits per SP 63 |
| `PolygonDecomposition` | Zone geometry → axis-aligned rectangles + auditable coverage metrics |

These are pure static functions with no I/O — fully testable with unit tests.

The polygon decomposition remains heuristic for arbitrary shapes, but it is no longer silent. Orthogonal thin and strongly concave zones now use an exact strip-based rectangle cover before the grid fallback is considered. Complex zones carry decomposition metrics (coverage ratio, over-coverage ratio, rectangle count, cell size, shortcut usage), and the canonical report aggregates those metrics into analysis provenance.

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
                        │  mass, cost, JSON  │
                        │  integration report│
                         └────────────────────┘
```

## Integration Boundary

The standalone project now exposes a canonical report artifact for external systems:

- schema: `contracts/aerobim-reinforcement-report.schema.json`
- payload: `ReinforcementExecutionReport`
- persistence port: `IReportStore`

This gives AeroBIM or other downstream consumers a stable machine-readable interface even before a fully validated IFC exporter exists.

The contract now carries two additional audit-oriented surfaces:

- `normativeProfile`: explicit normative profile id, jurisdiction, design code, and table-set version
- `analysisProvenance`: geometry and optimization provenance used for reproducibility and review

## Logging Boundary

For this standalone .NET extraction, structured logging uses the official `ILogger<T>` abstraction from DI rather than a custom logger port. This follows current Microsoft guidance for DI-based .NET applications while keeping Domain models free of framework dependencies.

## Testing Strategy

| Layer | Test Type | Framework |
|-------|-----------|-----------|
| Domain.Rules | Unit tests | xUnit + FluentAssertions |
| Domain.Models | Unit tests | xUnit + FluentAssertions |
| Application | Integration (mocked ports) | xUnit + NSubstitute |
| Infrastructure | Integration | xUnit + FluentAssertions |
| RevitPlugin | Manual (requires Revit) | — |
| ML | Unit + integration | pytest |

The infrastructure suite now includes an exact benchmark pack for CSP that checks bar-count gap, score gap, and waste-gap distribution against an exact small-instance reference.

## Future Roadmap

1. **Replace heuristic internals of the current CG implementation** with a true LP master + exact dual pricing or OR-Tools-backed branch-and-price
2. **Strengthen polygon decomposition further** toward exact clipping / coverage proofs for thin or highly concave zones
3. **Revit view filters** for color-coded zone visualization
4. **Multi-slab batch processing** across floors
5. **Export to IFC** for BIM collaboration
6. **Training pipeline** for U-Net on customer-specific isoline styles
