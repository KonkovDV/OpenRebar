<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/brand/openrebar-logo-dark.png">
    <img src="docs/brand/openrebar-logo.png" alt="OpenRebar" width="640">
  </picture>
</p>

# OpenRebar

<p align="center">
  <a href="https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml"><img src="https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://scorecard.dev/viewer/?uri=github.com/KonkovDV/OpenRebar"><img src="https://api.scorecard.dev/projects/github.com/KonkovDV/OpenRebar/badge" alt="OpenSSF Scorecard"></a>
  <img src="https://img.shields.io/badge/.NET-8.0-purple" alt=".NET 8">
  <img src="https://img.shields.io/badge/.NET-10.0-purple" alt=".NET 10">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License: MIT"></a>
</p>

English | [Русский](README.ru.md)

OpenRebar lays the reinforcement of a flat slab on the map the calculation already drew. Isolines in DXF, a calibrated PNG, or a field of required area come in. Bars, anchorage, laps, a schedule, a cutting plan, and an IFC file come out.

Same drawing. Same report.

Three places in the chain.

| Place | Role |
|---|---|
| Calculation program | Writes the required area |
| **OpenRebar** | Lays out the bars and checks that area |
| [AeroBIM](https://github.com/KonkovDV/AeroBIM) | Reads a documentation pack and sets its own pack flag |

Other repositories on the same GitHub account are their own work.

Libraries build for .NET 8 and .NET 10. The CLI is .NET 10. The Revit add-in is two builds, chosen by the process runtime: `RevitNet8` while Revit is on .NET 8, and `RevitNet10` on .NET 10.

## Contents

- [What a run looks like](#what-a-run-looks-like)
- [Status](#status)
- [Quick start](#quick-start)
- [What you get](#what-you-get)
- [How it is built](#how-it-is-built)
- [How a run behaves](#how-a-run-behaves)
- [Examples](#examples)
- [Optional ML lane](#optional-ml-lane)
- [Revit](#revit)
- [Supply chain](#supply-chain)
- [Documents](#documents)
- [License](#license)

## What a run looks like

Ø20 at 150 mm, cover 30 mm, slab 220 mm thick. The effective depth is 180 mm: `220 − 30 − 20 / 2`.

<p align="center">
  <img src="docs/images/simple-slab-plan.svg" alt="Plan of the house slab: 26 bars across 6000 by 4000 mm, pale bands at both free ends, and an orange rim" width="760">
</p>

That is the house slab, `examples/dxf/simple-slab`, drawn from its own IFC and verification files. 26 bars run the 6000 mm span. The straight bar stops 130 mm from each free face, so the hook crown sits on the cover line, 40 mm in. The last row sits at 3885 mm, 170 mm from the one before it. The pale bands are cells within anchorage reach of a free end: `edgeDevelopmentAreaM2` 5.46. The orange rim is the cells no bar reaches: `realDeficitAreaM2` 2.55. Together, `underReinforcedAreaM2` 8.01. The run exits 2 and leaves every file on the table.

Each bar carries `hookOutsideConcrete`. The semicircle rises 180 mm from the axis at 40 mm, and the bar surface crosses the top face by 10 mm. That exit is vertical, so the plan draws the bars and the cells.

```mermaid
flowchart LR
  M["DXF, PNG, or the area field"] --> B["Bars, anchorage, laps"]
  B --> C["Area check, cell by cell"]
  C --> O["Schedule, cut, IFC"]
```

## Status

The five commands under [Quick start](#quick-start): **473/473 tests passing**.

| | |
|---|---|
| Report | `schemaVersion` 2.1.0, contract `OpenRebar.reinforcement.report.v2`. Unknown fields are rejected |
| Norms | SP 63.13330.2018, profile `ru.sp63.2018`, tables `ru.sp63.2018.tables.v3` |
| Area check | Cell grid. `Passed` only when the deficit is zero. A hard clash is recorded beside that gate |
| `checks` | `providedArea` follows the area check. Crack width and deflection stay `NotEvaluated` when the input is only required area |
| Cutting | Column generation (HiGHS). `wastePercent` and `estimatedCost` (null when a used stock length has no price) |
| IFC | IFC4. One bar type per diameter and steel class, a swept disk of radius d/2, a placement on the axis |
| Revit | Straight bars in a live document |

The numbers on this page are the ones the tests hold. See [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md).

## Quick start

.NET SDK 8 and 10. Python 3.11 for the CI scripts and the optional ML lane.

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

One slab:

```bash
dotnet run --project src/OpenRebar.Cli --configuration Release -- examples/dxf/simple-slab/input.dxf --thickness 220 --cover 30 --slab-width 6000 --slab-height 4000 --layer BottomX
```

Exit 2: the area check failed and the files were still written. Exit 1: input or IO. Exit 3: a critical detailing error, such as a bar that cannot be bought or lapped, or a supported edge on a non-rectangular outline (`edgesRequireRectangularSlab`).

Flags: `--legend`, `--catalog`, `--ml-url`, `--slab-width`, `--slab-height`, `--thickness`, `--cover`. `--ml-url` talks to that host. Loopback is allowed on its own. A redirect to another host is refused.

## What you get

| File | Role |
|---|---|
| `*.result.json` | The report |
| `*.schedule.csv` | Positions and mass |
| `*.aerobim.json` | A short summary for a downstream checker |
| `*.reinforcement.ifc` | IFC4 bars |
| `*.verification.png` | One pixel per cell: green holds, red is short |
| `*.verification.json` | The short cells, with their margin and status |

On simple-slab, Ø20 and 30 mm cover, `effectiveDepthMm` and `effectiveDepthByLayer.BottomX` are both 180. That is `220 − 30 − 20 / 2`: thickness, minus cover, minus half the bar. A layer with no bars stays out of the map.

The report keeps the profile, the table version, and the optimizer. `checks[].status` is `Passed`, `Failed`, or `NotEvaluated`.

## How it is built

```
Domain ← Application ← Infrastructure ← CLI and Revit plugin
```

Domain has no NuGet packages. NetTopologySuite, HiGHS, xBIM, and ImageSharp stay in Infrastructure, behind ports in `src/OpenRebar.Domain/Ports/`. The Revit API is referenced only in `src/OpenRebar.RevitPlugin/` under `#if REVIT_SDK`.

| Port | Role |
|---|---|
| `IIsolineParser` | DXF and PNG zones |
| `IReinforcementCalculator` | Bars in a zone |
| `IRebarOptimizer` | 1D cutting |
| `IIfcExporter` | IFC4 |
| `IRevitPlacer` | Straight bars in a live document |
| `IImageSegmentationService` | Optional local HTTP bridge |
| `IStructuredLogger` | The run log; console by default |

The cutter used in a run is column generation. It records when the dual bound is `Proven`. Beside it sits `BestFitDecreasingOptimizer`: each bar goes into the open stock piece with the smallest remainder. With `--include-diagnostics`, an exception stack goes to the log and its SHA-256 to the report as `stackTraceSha256`.

## How a run behaves

One drawing, one layer. A project file can name four.

A `Supported` or `Continuous` edge is a side of the rectangle. Any other outline stops with `edgesRequireRectangularSlab` and exit 3, before a bar is placed. Free edges of an oblique slab stay on the real outline. An oblique bar steps outward by 10 mm and is clipped so the end sits inside the contour.

The later piece of a lap sits one diameter off the design axis: beside it when `laps.representation` is `inPlane`, above it when `stacked`. A profile that omits the field keeps `inPlane`. Anchorage ends at the working area. A hook is the arc along the axis. The straight tail stays 0 until a profile sets `hooks.tailDiameters`.

The schedule is CSV. IFC4 carries a bar type, a swept disk, and a placement on the axis. Revit takes the straight bar; a hook, an L, or a U returns as an error. The schedule and the IFC are the record.

A PNG brings `--px-per-mm` and an origin. A frame above 25 million pixels is turned away from the header, before a single pixel is decoded.

On simple-slab the area figures are `edgeDevelopmentAreaM2` 5.46, `realDeficitAreaM2` 2.55, `underReinforcedAreaM2` 8.01.

## Examples

- `examples/dxf/simple-slab/` — free edges, exit 2, files written
- `examples/png/simple-slab/` — the same slab from a calibrated PNG
- `examples/fe-field/supported-slab.csv` with `supported-slab.project.json` — slab on walls, area check passes

Regenerate snapshots with `tools/examples/generate_expected_outputs.ps1` or `.sh`. A changed figure is explained in the pull request.

Draw the plan of any run with `python tools/examples/render_plan.py <folder>/input out.svg`. It reads the IFC, the verification cells, and the report that the run wrote into that folder.

## Optional ML lane

```bash
cd ml
python -m pip install --require-hashes -r requirements.locked.txt
python -m pytest tests -q
python scripts/validate_model_manifest.py models/MANIFEST.json
```

CI installs that lock with hashes on Python 3.11. A PNG is read by an exact match to the legend color. `--ml-url` is there when you already have a service of your own.

## Revit

Copy `addin/RevitNet8` next to the `net8.0-windows` build, or `addin/RevitNet10` next to the `net10.0-windows` build. Bar axes sit at cover + d/2. The inner direction of a face lies on the outer one. Top cover is read from `CLEAR_COVER_TOP`. See [RELEASE_POLICY.md](RELEASE_POLICY.md).

## Supply chain

NuGet restore is locked. The ML lane installs `ml/requirements.locked.txt` with hashes. Scorecard runs on `main`. A release builds an SBOM and attests it against the CLI archive.

## Documents

- [docs/README.md](docs/README.md) — map
- [docs/architecture.md](docs/architecture.md)
- [docs/NORMATIVE_TRACEABILITY.md](docs/NORMATIVE_TRACEABILITY.md) — clause, code, test
- [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md)
- [docs/SOTA_2026.md](docs/SOTA_2026.md)
- [docs/AUDIT_2026-10-18.md](docs/AUDIT_2026-10-18.md)
- [docs/AUDIT_2026-10-20.md](docs/AUDIT_2026-10-20.md)
- [docs/brand/openrebar-brand-sheet.jpg](docs/brand/openrebar-brand-sheet.jpg)
- [docs/OPENREBAR_AGENT_PLAN_r10_2026_10_14.md](docs/OPENREBAR_AGENT_PLAN_r10_2026_10_14.md)
- [CHANGELOG.md](CHANGELOG.md), [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), [CITATION.cff](CITATION.cff)

## License

MIT. See [LICENSE](LICENSE).
