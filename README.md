# OpenRebar

[![CI](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml/badge.svg)](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/KonkovDV/OpenRebar/badge)](https://scorecard.dev/viewer/?uri=github.com/KonkovDV/OpenRebar)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

English | [Русский](README.ru.md)

OpenRebar turns a required-area map of a flat reinforced-concrete slab into bars, anchorage, laps, a schedule, a cutting plan, and an IFC file. The map arrives as a DXF isoline drawing, a calibrated PNG, or a numeric field. The core is deterministic. A language model does not place bars and does not set a pass/fail flag.

The libraries build for .NET 8 and .NET 10. The CLI is .NET 10. The Revit add-in is two builds, chosen by the process runtime: `RevitNet8` while Revit is still on .NET 8, and `RevitNet10` on .NET 10. Engineering rules are tested without Revit.

**What this is not.** It is not a finite-element solver, not a trained segmenter, and not a crack-width or deflection calculator. It does not place bent bars in Revit. It does not claim a steel saving. The optional `ml/` service ships with an empty model manifest and stays offline.

## Status

Counted regression, the five commands under [Quick start](#quick-start): **453/453 tests passing**.

| Surface | Now |
|---|---|
| Report | `schemaVersion` 2.1.0, contract `OpenRebar.reinforcement.report.v2` |
| Norms | SP 63.13330.2018, profile `ru.sp63.2018`, tables `ru.sp63.2018.tables.v3` |
| Area check | Cell grid. `Passed` only at zero deficit. A hard clash does not replace that gate |
| `checks` | `providedArea` follows the area check. Crack width and deflection stay `NotEvaluated` |
| Cutting | Column generation (HiGHS) with a recorded provenance. Waste percent and `estimatedCost` (null when the catalogue has no price) |
| IFC | IFC4. One bar type per diameter and steel class, a swept disk of radius d/2, a placement on the axis. CI checks those entity names |
| Revit | Straight bars only. A hook, L, or U is an error, not a straight line |

The worked example `examples/dxf/simple-slab` exits 2. Edges are free, the required area runs to the edge, and a hook is not credited. That failure is the regression, not a bug in the snapshot.

## Stage

The examples are synthetic fixtures. They exercise the pipeline and the regression. They are not a measured pilot in a design office, and they are not a certificate.

Shown on those fixtures:

- a free-edge slab whose area check fails, and a supported slab whose area check passes
- a report that rejects unknown fields
- an IFC file whose bar type, body, and placement the CI check can see

Not shown here:

- a profile signed by an organisation, or a scan of every cited clause
- a trained PNG segmenter
- bent bars placed in a live Revit model
- an IFC file opened again by another program
- a saving of steel or money

If this page and the tests disagree, the tests win. The rule for publishing a number is [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md).

## Where it sits

| Role | Program | Boundary |
|---|---|---|
| Required area | A calculation program writes As,req | OpenRebar does not solve the slab |
| Layout and area check | **OpenRebar** | Does not accept the documentation pack |
| Pack check | [AeroBIM](https://github.com/KonkovDV/AeroBIM) | Reads a model, a sheet, a schedule, and text. Sets its own pack flag. Does not design the bars |

Scheduling engines, marketplaces, and research notes under the same GitHub account are other products.

## Pipeline

1. Read the isoline file. A DXF entity that is not a zone is counted, not dropped. A PNG is measured from its header before decode.
2. Classify zones and record decomposition coverage.
3. Place bars. Spacing on a face is measured on the combined layer. Anchorage and lap lengths come from the versioned tables. Hook credit is 0. The default hook tail is 0.
4. Cut stock. A bar that cannot be bought or lapped is a critical error.
5. Check provided area against required area and write `checks`.
6. Write `*.result.json`, `*.schedule.csv`, `*.aerobim.json`, and `*.reinforcement.ifc`.

## SOTA 2026, stated as a gap

The comparison lives in [docs/SOTA_2026.md](docs/SOTA_2026.md). The short form:

| Topic | In this repository | Not in this repository |
|---|---|---|
| Area certificate | Per-cell margin and a deficit polygon | A claim that the layout is optimal |
| Provenance | Profile id, table version, optimizer id, parameter source | A norm typed from memory |
| Cutting | Column generation plus a recorded waste and purchase cost | A measured saving in percent or money |
| IFC | IFC4 bar type, swept disk, placement, IDS entity names in CI | IFC 4.3 lap-splice encoding, a viewer round-trip |
| Image segmentation | Exact RGB match. The U-Net path has no checkpoint | SAM2, DINOv2, a published mIoU |
| Layout search | Zone rules and a spacing check | GNN, reinforcement learning, a physics-informed net |
| Serviceability | `NotEvaluated` for crack width and deflection | A PINN or a code check of those limits |

## Quick start

Requirements: .NET SDK 8 and 10, Python 3.11 for the CI scripts and the optional ML lane.

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

Exit 2 means the area check failed and the files were still written. Exit 1 is an input or IO failure. Exit 3 is a critical detailing error.

Useful flags: `--legend`, `--catalog`, `--ml-url`, `--slab-width`, `--slab-height`, `--thickness`, `--cover`.

## Outputs

| File | Role |
|---|---|
| `*.result.json` | Canonical report. Unknown properties are rejected |
| `*.schedule.csv` | Positions and mass. Spreadsheet-safe quoting |
| `*.aerobim.json` | Short summary for a downstream checker |
| `*.reinforcement.ifc` | IFC4 bars |

The report keeps the normative profile, the table version, optimizer provenance, kerf-aware `wastePercent`, and `estimatedCost`. A new top-level field requires a schema bump. `checks[].status` is `Passed`, `Failed`, or `NotEvaluated`.

## Layout

```
Domain ← Application ← Infrastructure ← CLI and Revit plugin
```

Domain has no NuGet packages. NetTopologySuite, HiGHS, xBIM, and ImageSharp stay in Infrastructure behind ports in `src/OpenRebar.Domain/Ports/`. The Revit API is referenced only in `src/OpenRebar.RevitPlugin/` under `#if REVIT_SDK`.

| Port | Role |
|---|---|
| `IIsolineParser` | DXF and PNG zones |
| `IReinforcementCalculator` | Bars in a zone |
| `IRebarOptimizer` | 1D cutting stock |
| `IIfcExporter` | IFC4 |
| `IRevitPlacer` | Straight bars in a live document |
| `IImageSegmentationService` | Optional HTTP bridge. Offline. No hosted model |

Column generation is the production cutter. It records when the dual bound is `Proven`. The fallback class is `FirstFitDecreasingOptimizer`; its loop keeps the open bar with the smallest remainder. Neither one is a claim of minimum waste on a real project.

## Known limits

- One drawing is one layer. A project file can name up to four drawings.
- Anchorage stops at the working area. A hook is the centerline arc and has no straight tail until a profile sets `hooks.tailDiameters`. Couplers are not modeled. Openings are counted and are still inside the checked area.
- Column generation packs one stock length at a time.
- The schedule is CSV. XLSX is not written.
- IFC is IFC4. It is not an IFC 4.3 splice model, and it has not been round-tripped through a Revit importer in this repository.
- Revit placement of a hook, L, or U is refused. The schedule and the IFC remain the record.
- A PNG needs calibration (`--px-per-mm` and an origin). Frames above 25 million pixels are rejected before decode.
- `examples/dxf/simple-slab` at Ø20 and 30 mm cover: `edgeDevelopmentAreaM2` 6.12, `realDeficitAreaM2` 0.99, `underReinforcedAreaM2` 7.11.

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

CI installs that lock with hashes on Python 3.11. There is no call to a hosted model. An empty manifest means the service has no checkpoint to run.

## Revit

Copy `addin/RevitNet8` next to the `net8.0-windows` build, or `addin/RevitNet10` next to the `net10.0-windows` build. Bar axes sit at cover + d/2. The inner direction of a face lies on the outer one. Top cover is read from `CLEAR_COVER_TOP`. See [RELEASE_POLICY.md](RELEASE_POLICY.md).

## Supply chain

- NuGet restore is locked.
- The ML lane installs `ml/requirements.locked.txt` with hashes and does not call a hosted model.
- Scorecard runs on `main`. A release builds an SBOM and attests the published files.
- ImageSharp stays on 3.1.12. Version 4 needs a license key. Five advisories on that line are accepted because that line has no patch. Any other high finding still fails the audit.

## Documents

- [docs/README.md](docs/README.md) — map
- [docs/architecture.md](docs/architecture.md)
- [docs/NORMATIVE_TRACEABILITY.md](docs/NORMATIVE_TRACEABILITY.md) — clause, code, test
- [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md) — how a public number is earned
- [docs/SOTA_2026.md](docs/SOTA_2026.md) — gap list
- [docs/OPENREBAR_AGENT_PLAN_r10_2026_10_14.md](docs/OPENREBAR_AGENT_PLAN_r10_2026_10_14.md) — current queue
- [CHANGELOG.md](CHANGELOG.md), [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), [CITATION.cff](CITATION.cff)

April 2026 audits under `docs/archive/` are history.

## License

MIT. See [LICENSE](LICENSE).
