# Architecture

## Scope

This page describes the program as it is built.

For executable validation expectations, use [VALIDATION_BASELINE.md](VALIDATION_BASELINE.md).

Dated April 2026 audits live in [archive/](archive/) and are not the source of truth.

## Overview

OpenRebar lays out slab reinforcement. The SP 63 rules, the geometry, and the cutting run without Revit and without reading a file. Domain owns the ports. Application runs the pipeline. Infrastructure implements the ports. The CLI and the Revit add-in wire them. A Python process, when one is used, stays outside Domain.

## Layer Dependency Graph

```
Domain ← Application ← Infrastructure ← CLI and Revit plugin
```

- **Domain**: no NuGet packages. Models, ports, and the SP 63 rules.
- **Application**: the pipeline and the use cases, on Domain ports only.
- **Infrastructure**: one adapter per port. `ServiceCollectionExtensions` registers them.
- **CLI and Revit plugin**: each composes the services and runs the pipeline. The Revit API stays under `#if REVIT_SDK`.

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

`IRevitPlacer` is a Domain port. `RevitRebarPlacer` lives in the plugin and compiles only with the Revit SDK. `StubRevitPlacer` lets the whole pipeline run in tests without Revit.

### 4. Optimization as Bin-Packing / Cutting Stock

Rebar cutting is a one-dimensional cutting-stock problem. Two optimizers sit behind `IRebarOptimizer`:

1. `ColumnGenerationOptimizer` is the one a run uses. The restricted master is an LP on HiGHS, pricing is a bounded-knapsack dynamic program, and the integer plan comes from largest-remainder rounding plus repair. The report records each of those, the dual bound, and whether a fallback ran. On the house slab the bound is `Proven` with a gap of 0.
2. `BestFitDecreasingOptimizer` is the floor beside it. Each bar goes into the open stock piece with the smallest remainder. It is constructed directly and is not registered in dependency injection.

A small instance uses an exact discrete search.

### 5. Supplier Catalogs

`ISupplierCatalogLoader` reads a JSON or CSV catalog through `--catalog`. Without one, the stock is the Russian market set: 6000, 9000, 11700, and 12000 mm. A bad row fails the load instead of being skipped.

## Normative Rules Engine

All SP 63 / GOST calculations are in `OpenRebar.Domain.Rules/`:

| Module | Responsibility |
|--------|---------------|
| `AnchorageRules` | Anchorage length (l_an), lap splices, bond stress lookup |
| `ReinforcementLimits` | Min/max reinforcement ratio, spacing limits per SP 63 |
| `PolygonDecomposition` | Zone geometry → axis-aligned rectangles + auditable coverage metrics |

These are pure static functions with no I/O — fully testable with unit tests.

Normative table values come from the embedded resource `ru.sp63.2018.tables.v3` through a domain registry. The report records that version.

Decomposition of an arbitrary shape is a heuristic, and it reports itself. Thin orthogonal and strongly concave zones take an exact strip cover before the grid fallback. A complex zone carries its coverage ratio, over-coverage ratio, rectangle count, cell size, and shortcut use, and the report sums them in `analysisProvenance`.

A complex zone passes a quality gate: coverage at least 0.94, over-coverage at most 0.25. A miss is a diagnostic. `PipelineInput.DecompositionQualityGate.TreatViolationsAsCritical` turns it into a stop.

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

Logging goes through the Domain port `IStructuredLogger`. Infrastructure writes it to the console with `ConsoleStructuredLogger`. With `--include-diagnostics`, an exception stack goes to that log and only its SHA-256 goes to the report.

## Testing Strategy

| Layer | Test Type | Framework |
|-------|-----------|-----------|
| Domain.Rules | Unit tests | xUnit + FluentAssertions |
| Domain.Models | Unit tests | xUnit + FluentAssertions |
| Application | Integration (mocked ports) | xUnit + NSubstitute |
| Infrastructure | Integration | xUnit + FluentAssertions |
| RevitPlugin | Manual (requires Revit) | — |
| ML | Unit + integration | pytest |

The infrastructure suite holds an exact benchmark pack for cutting: bar-count gap, score gap, and waste gap against an exact reference on small instances.

The domain suite holds golden tests for the normative tables and the report defaults, so bond and design-strength lookups stay where the tables put them.

Geometry predicates share one `GeometryTolerance` policy. Its `Computational` profile owns the epsilons for segment intersection, point-in-polygon, and DXF polygon stitching.

## Next

1. Branch-and-price, so the integer plan closes the last gap the rounding leaves.
2. Exact clipping proofs for thin and strongly concave zones.
3. Bent bars in Revit: today the add-in takes the straight bar.
4. Revit view filters for color-coded zone review.

Test counts and audit verdicts live in [VALIDATION_BASELINE.md](VALIDATION_BASELINE.md) and the dated audit notes.
