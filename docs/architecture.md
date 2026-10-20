# Architecture

## Scope

This page describes the program as it is built.

For executable validation expectations, use [VALIDATION_BASELINE.md](VALIDATION_BASELINE.md).

Dated April 2026 audits live in [archive/](archive/) and are not the source of truth.

## Overview

OpenRebar lays out slab reinforcement. The SP 63 rules, the geometry, and the cutting run without Revit and without reading a file. Domain owns the ports. Application runs the pipeline. Infrastructure implements the ports. The CLI and the Revit add-in wire them. A Python process, when one is used, stays outside Domain.

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

### 2. PNG and the optional Python service

`PngIsolineParser` matches each pixel to a legend colour by exact RGB, then groups the matches into connected components. The Python service is FastAPI and is used only when a caller passes a URL. The model manifest has no checkpoint, so that path is not a trained segmenter. The client allows loopback and the named host, and it refuses a redirect to another host.

### 3. Revit Placer as Domain Port

`IRevitPlacer` is defined in Domain as a pure interface. The real Revit implementation (`RevitRebarPlacer`) lives in the plugin project and is only compiled when the Revit SDK NuGet is available. A `StubRevitPlacer` enables full pipeline testing without Revit.

### 4. Optimization as Bin-Packing / Cutting Stock

Rebar cutting is a one-dimensional cutting-stock problem. Two optimizers sit behind `IRebarOptimizer`:

1. `ColumnGenerationOptimizer` is the one a run uses. It is an LP relaxation, pricing, and rounding. The report records the optimizer id, the LP strategy, the integerization, and whether a fallback was used. It is not a complete branch-and-price solver.
2. `BestFitDecreasingOptimizer` is the floor beside it. Each bar goes into the open stock piece with the smallest remainder. It is constructed directly and is not registered in dependency injection.

A small instance uses an exact discrete search.

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

Normative table values come from the embedded resource `ru.sp63.2018.tables.v3` through a domain registry. The report records that version.

The polygon decomposition remains heuristic for arbitrary shapes, but it is no longer silent. Orthogonal thin and strongly concave zones now use an exact strip-based rectangle cover before the grid fallback is considered. Complex zones carry decomposition metrics (coverage ratio, over-coverage ratio, rectangle count, cell size, shortcut usage), and the canonical report aggregates those metrics into analysis provenance.

The application pipeline now applies a decomposition quality gate for complex zones (default thresholds: coverage ratio >= 0.94 and over-coverage ratio <= 0.25). Violations are persisted as diagnostics in the execution report and can be promoted to fail-fast behavior via `PipelineInput.DecompositionQualityGate.TreatViolationsAsCritical` for strict QA lanes.

## Data Flow

```
                            ┌─────────────┐
                            │ LIRA-SAPR   │
                            │ isoline.dxf │
                            └──────┬──────┘
                                   │
                         ┌─────────▼──────────┐
                         │  IIsolineParser     │
                         │  DXF, PNG, or field │
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

The report is the contract a downstream reader takes:

- schema: `contracts/aerobim-reinforcement-report.schema.json`
- payload: `ReinforcementExecutionReport`
- persistence port: `IReportStore`

IFC4 writes one bar type per diameter and steel class, a swept disk of radius d/2, and a placement on the axis.

The contract carries:

- `normativeProfile`: profile id, jurisdiction, design code, and table-set version
- `analysisProvenance`: geometry and optimization provenance for a second reading

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

The domain suite now also includes golden tests for the normative profile resource and metadata defaults, so bond/design-strength lookups and report defaults stay aligned.

Geometry predicates now use a shared `GeometryTolerance` policy in the Domain model. The `Computational` profile centralizes low-level epsilon usage for segment intersection, point-in-polygon, and DXF polygon stitching so adapters do not drift on magic constants.

## Future Roadmap

1. **Replace heuristic internals of the current CG implementation** with a true LP master + exact dual pricing or OR-Tools-backed branch-and-price
2. **Strengthen polygon decomposition further** toward exact clipping / coverage proofs for thin or highly concave zones
3. **Complete the live Revit production boundary** beyond the currently validated standalone and testable host-independent surfaces
4. **Add richer Revit-side visualization and QA affordances** such as view filters for color-coded zone review
5. **Image reading stays on the legend color.** A checkpoint is not part of this repository.

## Claim Boundary

- This document describes current structure and boundary decisions.
- It should not be used as the source for test counts, audit verdicts, or release-readiness status.
- Time-bounded evidence belongs in the dated audit documents under `docs/`.
