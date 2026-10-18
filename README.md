# OpenRebar

[![CI](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml/badge.svg)](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/KonkovDV/OpenRebar/badge)](https://scorecard.dev/viewer/?uri=github.com/KonkovDV/OpenRebar)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

English | [Русский](README.ru.md)

OpenRebar lays out reinforcement for a flat reinforced-concrete slab from a map of required area. The map is a DXF isoline drawing, a calibrated PNG, or a numeric field. The run writes bars, anchorage, laps, a schedule, a cutting plan, and an IFC file.

Three places in the chain.

| Place | Role |
|---|---|
| Calculation program | Writes the required area |
| **OpenRebar** | Lays out the bars and checks that area |
| [AeroBIM](https://github.com/KonkovDV/AeroBIM) | Reads a documentation pack and sets its own pack flag |

Other repositories on the same GitHub account are other products.

The same input produces the same report.

Libraries build for .NET 8 and .NET 10. The CLI is .NET 10. The Revit add-in is two builds, chosen by the process runtime: `RevitNet8` while Revit is on .NET 8, and `RevitNet10` on .NET 10. The engineering rules are tested without Revit.

## Status

Counted regression, the five commands under [Quick start](#quick-start): **467/467 tests passing**.

| | |
|---|---|
| Report | `schemaVersion` 2.1.0, contract `OpenRebar.reinforcement.report.v2`. Unknown fields are rejected |
| Norms | SP 63.13330.2018, profile `ru.sp63.2018`, tables `ru.sp63.2018.tables.v3` |
| Area check | Cell grid. `Passed` only when the deficit is zero. A hard clash is recorded beside that gate |
| `checks` | `providedArea` follows the area check. Crack width and deflection stay `NotEvaluated` when the input is only required area |
| Cutting | Column generation (HiGHS). `wastePercent` and `estimatedCost` (null when a used stock length has no price) |
| IFC | IFC4. One bar type per diameter and steel class, a swept disk of radius d/2, a placement on the axis |
| Revit | Straight bars. A hook, an L, or a U is an error |

`examples/dxf/simple-slab` exits 2. The edges are free, the required area runs to the edge, and a hook is not credited. That failure is the regression.

The examples are fixtures for the tests. A design-office pilot and a certificate are still ahead of this page. If this page and the tests disagree, the tests win. A public number follows [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md).

Where the 2026 literature is ahead of this repository is written in [docs/SOTA_2026.md](docs/SOTA_2026.md).

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

On simple-slab, Ø20 and 30 mm cover, `effectiveDepthMm` and `effectiveDepthByLayer.BottomX` are both 180. That is `220 − 30 − 20 / 2`: thickness, minus cover, minus half the bar. A layer with no bars is left out of the map. `estimatedCost` is null because the example catalogue has no price.

The report records the profile, the table version, and the optimizer. `checks[].status` is `Passed`, `Failed`, or `NotEvaluated`.

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
| `IImageSegmentationService` | Optional local HTTP bridge. No hosted model |

The cutter used in a run is column generation. It records when the dual bound is `Proven`. Beside it sits `BestFitDecreasingOptimizer`: each bar goes into the open stock piece with the smallest remainder. A diagnostic stack is stored as `stackTraceSha256`. The text stays in the log.

## Limits

One drawing is one layer. A project file can name up to four drawings.

A `Supported` or `Continuous` edge is a side of the bounding box. On any other outline the run stops with `edgesRequireRectangularSlab` and exit 3. Free edges of an oblique slab stay on the outline. An oblique bar is stepped outward by 10 mm and then clipped, so the end stays inside the contour.

The later piece of a lap is drawn one diameter off the design axis: beside it when `laps.representation` is `inPlane`, above it when `stacked`. A profile that omits the field keeps `inPlane`. Anchorage stops at the working area. A hook is the centerline arc. The straight tail stays 0 until a profile sets `hooks.tailDiameters`. Couplers are not modeled.

The schedule is CSV. IFC here is IFC4: bar type, swept disk, placement. An IFC 4.3 splice, and a round-trip through another program, are still ahead. Revit accepts a straight bar. A hook, an L, or a U is refused. The schedule and the IFC remain the record.

A PNG needs `--px-per-mm` and an origin. A frame above 25 million pixels is rejected from the header, before decode.

On simple-slab the area figures are `edgeDevelopmentAreaM2` 6.12, `realDeficitAreaM2` 0.99, `underReinforcedAreaM2` 7.11.

## Examples

- `examples/dxf/simple-slab/` — free edges, area check fails, exit 2
- `examples/png/simple-slab/` — the same slab from a calibrated PNG
- `examples/fe-field/supported-slab.csv` with `supported-slab.project.json` — slab on walls, area check passes

Regenerate snapshots with `tools/examples/generate_expected_outputs.ps1` or `.sh`. Every changed figure belongs in the pull request.

The batch corpus is outside the push lane. Without `OPENREBAR_BATCH_CORPUS_ROOT` the corpus job writes `corpus: NotEvaluated`.

## Optional ML lane

```bash
cd ml
python -m pip install --require-hashes -r requirements.locked.txt
python -m pytest tests -q
python scripts/validate_model_manifest.py models/MANIFEST.json
```

CI installs that lock with hashes on Python 3.11. The manifest is empty: there is no checkpoint to run, and there is no call to a hosted model. Without a checkpoint, a PNG is read by exact color match.

## Revit

Copy `addin/RevitNet8` next to the `net8.0-windows` build, or `addin/RevitNet10` next to the `net10.0-windows` build. Bar axes sit at cover + d/2. The inner direction of a face lies on the outer one. Top cover is read from `CLEAR_COVER_TOP`. See [RELEASE_POLICY.md](RELEASE_POLICY.md).

## Supply chain

NuGet restore is locked. The ML lane installs `ml/requirements.locked.txt` with hashes. Scorecard runs on `main`. A release builds an SBOM, attests the published files, and attests that SBOM against the CLI archive. ImageSharp stays on 3.1.12: version 4 needs a license key, and the five advisories on 3.1.12 have no patch on that line. Any other high finding still fails the audit.

## Documents

- [docs/README.md](docs/README.md) — map
- [docs/architecture.md](docs/architecture.md)
- [docs/NORMATIVE_TRACEABILITY.md](docs/NORMATIVE_TRACEABILITY.md) — clause, code, test
- [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md)
- [docs/SOTA_2026.md](docs/SOTA_2026.md)
- [docs/OPENREBAR_AGENT_PLAN_r10_2026_10_14.md](docs/OPENREBAR_AGENT_PLAN_r10_2026_10_14.md) — work queue
- [CHANGELOG.md](CHANGELOG.md), [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), [CITATION.cff](CITATION.cff)

April 2026 audits under `docs/archive/` are history.

## License

MIT. See [LICENSE](LICENSE).
