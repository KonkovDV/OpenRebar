# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- SP 63 table 6.14 from Amendment 1: Rs is A400 340 MPa and B500 415 MPa. A500 stays 435 MPa, so the A500C examples keep the same bar lengths and masses. Rsc is stored from the TECHNO NICOL copy (A400 350 MPa). A second public transcription prints A400 Rsc as 340, and the official order text was not opened. Anchorage still uses Rs.

### Fixed

- CI looks up `--no-build` assemblies in `bin/Release`, where the solution build writes them. The push lane no longer fails the missing batch corpus; that check runs on the weekly schedule and on manual dispatch when a corpus path is configured. The ML lock uses CPU PyTorch wheels.
- Dependabot will not open ImageSharp 4 or FluentAssertions 8. `Microsoft.Extensions.*` updates are grouped. ImageSharp stays on 3.1.x until the distribution model (Q-5) is chosen.

### Added

- The April 2026 audits and execution plans moved to `docs/archive/`. They are historical and are not the source of truth. The current plan is `docs/OPENREBAR_AGENT_PLAN_r7_2026_10_02.md`.
- A slab edge is `Free`, `Supported`, or `Continuous`. An omitted edge is `Free`, and the report says `EdgeKindDefaulted`. Anchorage may run into a support; a hook is still not credited. The coverage check lists `edgeDevelopmentAreaM2` apart from `realDeficitAreaM2`. `examples/fe-field/supported-slab.csv` is a slab on walls and passes.
- Report `stages[]` with stable `snake_case` reason codes, and `boundStatus` (`Proven` or `NotProven`) on each cutting result. A dual bound is omitted until column generation has converged.
- PNG calibration (`--px-per-mm`, `--origin-px`, `--roi-px`) and a closed-polyline DXF reader. The simple-slab PNG twin matches the DXF zone.
- CLI exit codes: 0 passed, 1 input or IO, 2 verification failed, 3 partial result. Stack traces are written only with `--include-diagnostics`.
- CI installs the committed Python lockfile. IFC example validation runs nightly and on export pull requests.
- A 50 mm grid checks provided reinforcement area. `Passed` requires zero under-coverage. A removed bar fails the check and records the deficit box.
- Area legends (`AsLegend`) reject overlapping, gapped, unordered, and too-similar color classes with `LEGEND_*` codes. Zones can carry required, background, and delta area.
- A project file (`--project` or `--design`) or repeated `--layer-input` supplies one to four layers, each with its own drawing and optional background mesh. A single drawing remains a single layer.
- Additional bars are chosen as the lightest diameter that covers the area above the background mesh. If none can, the result is `NeedsHumanDecision` instead of a tighter spacing.
- A layer with a background mesh lays that mesh as bars and places additional bars only where the required area is higher. The coverage check counts those placed bars. A layer without a background is unchanged. Spacing modes `s/2` and `explicit` stay `needs_human_decision`.
- When a layer has a background mesh, additional bars use one grid for the whole layer, so adjacent zones do not place lines closer than that spacing.
- Report contract `2.0.0` (`OpenRebar.reinforcement.report.v2`) always lists four layers. Missing layers are `NotProvided`. The schedule CSV is sectioned by layer, with a diameter mass summary. See `docs/AEROBIM_MIGRATION_2.md`.
- Company profiles live in `profiles/generic.json`. A profile may tighten a norm and may not weaken it. `parameterSources` records whether each value came from the profile, the project, or the built-in default. CLI: `profile init`, `profile validate`, `profile diff`.
- A CSV table of plate elements can be read as a required-As field, including a LIRA-SAPR AS1–AS4 preset. Values are stored in mm²/m. Rectangular zones are built from that field, with an outward margin of half an element, and the coverage check uses the element value under each sample. XLSX is not read yet.
- A zone keeps interior rings from a DXF hatch or a PNG contour. Bars and the coverage check skip those holes. Boolean geometry uses NetTopologySuite 2.6 on a 0.1 mm grid.
- The working area is the slab inset by `--edge-cover` (or `coverEdgeMm` in the project file) minus each opening grown by `--opening-clearance`. Both default to 0, so an omitted value keeps the previous bar lengths. Bars are placed only inside that area.
- Overlapping classes of one layer are split into faces. The shared face keeps the class with the greater required area and is reinforced once. The report warning starts with `ClassOverlap`.
- Bars are grouped into runs. Adjacent lines whose lengths differ by at most 50 mm share one length, the outer end, so a bar is never shortened. The 20×20 cell grid no longer covers a zone, and the decomposition quality gate no longer stops the pipeline.
- On a slanted edge, bar length is rounded up to 10 mm and a run splits where that rounded length changes. Every bar in a run has the same length and still covers the zone.
- The report lists `clashes`. A bar that leaves the working area, or that enters an opening, is `hard`. So is a side gap below `max(d, 25 mm)`, and an X/Y crossing whose diameters do not fit between the covers. A lap share above the profile limit is `soft`. These findings do not by themselves fail the area check.
- A bar longer than the longest stock length is cut into lapped pieces. The lap length comes from the existing SP 63 calculation. Joints in one run are staggered so a section of 1.3 lap lengths contains at most half the bars; otherwise the lap uses the full-section factor and the report says so. The report lists `laps`. Couplers are not modeled. The clear distance between laps from the amendment to clause 10.3.30 is still not checked.
- Anchorage is part of the bar geometry and stops at the working area. `Start` and `End` are the placed ends. The Revit line uses those ends. An end that cannot take the full anchorage is marked from the profile (`NeedsHook` in `generic`) and the report carries a warning. The area check then starts its development ramp at that end, so a bar stopped on the slab edge can fail the check.
- A short end is a bend. The schedule and the cutting list add the centerline arc of the mandrel from SP 63 clause 10.3.33 and do not add a straight tail. Shape codes stay internal (`00`, `H`, `L`, `U`). `ends.shapeStandard` accepts only `internal`. A lap piece keeps only the bend of the end it still has, and that arc counts against the stock length. An unknown steel class uses the larger periodic mandrel.
- The schedule is a specification. Each position is named `Ø20 A500C l = 6565`, with unit mass and the shape in the note. The designation column is empty. A following sheet lists steel mass by class and diameter. `schedule.template` accepts only `gost-21.501`. XLSX is not written.
- Provided area ramps from 0 at the physical end of an additional bar to the full `A/s` at one anchorage length. A bar with no stored anchorage keeps full capacity. Background bars are not ramped. The certificate adds `underReinforcedCells`, `excessSteelKg`, and a per-layer summary. `Passed` still requires zero under-reinforced area.
- A run also writes `*.verification.png` (short cells in red) and `*.verification.json` (only the short cells).
- OpenSSF Scorecards workflow (`.github/workflows/scorecards.yml`) with SARIF upload to GitHub code scanning
- Citation metadata (`CITATION.cff`) for academic and technical referencing
- Funding metadata (`.github/FUNDING.yml`) for GitHub Sponsors discoverability
- **OpenRebar rebrand**: full namespace, project, and folder rename from the legacy project name to OpenRebar
- **P3 ML smoke coverage**: synthetic dataset tests for training dataset loading, one-epoch CPU training, evaluation metrics, and ONNX export
- **P1 Revit boundary**: host floor structural validation (category, compound structure, min thickness)
- **P1 Revit boundary**: rebar tag creation pass with `IndependentTag.Create` and midpoint positioning
- **P1 Revit boundary**: bending detail tracking per unique `RebarShape`
- **P3 ML training pipeline**: `ml/src/training/` module with dataset loader, augmentation, train loop
- **P3 ML evaluation**: per-class IoU, mean IoU, pixel accuracy, confusion matrix
- **P3 ONNX export**: `export_onnx.py` for CPU-only C# inference via OnnxRuntime
- **P3 Benchmarks**: inference latency, parameter count, batch throughput, ONNX exportability tests
- **P3 Batch benchmark rail**: real-adapter application test pack with generated DXF slabs, persisted reports, and FFD quality-envelope checks
- **P3 Corpus-ready batch rail**: optional manifest-driven fixture test for production slab batches with persisted report checks and configurable FFD regression envelopes
- **Academic geometry hardening**: complex-zone decomposition now persists coverage and over-coverage metrics
- **Academic optimization TEVV**: exact small-instance bar-count cross-checks for the column-generation optimizer
- **Academic optimization TEVV**: benchmark pack for score-gap and waste-gap distribution on small CSP instances
- **Optimization hardening**: exact discrete search path for tiny mixed-stock instances
- **Canonical report provenance**: normative profile + geometry/optimization provenance in `*.result.json`
- **Normative data hardening**: SP 63 lookup tables moved into versioned embedded resource `ru.sp63.2018.tables.v1`
- **Normative TEVV**: golden tests for bond stress, design strength, periodic-profile lookup, linear mass, and metadata defaults
- CLI arguments `--slab-width` and `--slab-height` for configurable slab footprint
- CLI boundary validation for numeric arguments (thickness, cover, slab dimensions)
- CLI integration tests verifying exported artifacts and custom geometry
- CHANGELOG.md with Keep a Changelog format
- Release workflow with SBOM generation and artifact attestation
- JSON report schema validation test against `contracts/aerobim-reinforcement-report.schema.json`
- Linux CI hardening for mixed solution targets: workflow restore/build lanes now pass `EnableWindowsTargeting=true` for `net8.0-windows` Revit project compatibility on Ubuntu runners
- Python smoke hardening in CI/release workflows with explicit `PYTHONPATH` for stable `ml/src` imports
- Comprehensive multi-level audit report `docs/archive/COMPREHENSIVE_PROJECT_AUDIT_2026_04_25.md` with evidence-backed findings and remediation log

### Changed

- CLI no longer uses a hardcoded 30×20m demo slab footprint; geometry is parameterized
- CLI wraps pipeline execution in structured error handling with meaningful exit codes
- Validation story for cutting quality now distinguishes shipped generated/fixture-driven batch harnesses from still-missing production slab corpora
- Local CI reproduction hygiene now ignores `ml/.venv-ci-*` virtual environments by default

### Fixed

- Help text alignment for `--legend` option
- Windows Unicode path handling for ML image loading (`cv2.imread` replaced with Unicode-safe decode path in training and inference)
- Missing ONNX export dependencies in `ml/requirements.txt` (`onnx`, `onnxscript`) for `torch.onnx.export`
- `torch.export` ONNX dynamic-shape wiring now uses positional tuple specs compatible with PyTorch 2.11 single-input exports
- ONNX export default opset raised to `18`, matching the PyTorch 2.11 exporter implementation floor and avoiding downgrade-conversion warnings
- Python ML dependency graph corrected: `ezdxf` range moved to `>=1.0,<2.0` (0.19-0.99 does not exist on PyPI)
- FastAPI upload dependency gap fixed by adding `python-multipart` to `ml/requirements.txt`
- CI/release SBOM step pin corrected to an existing `anchore/sbom-action` commit SHA
- Intermittent CI import failure `ModuleNotFoundError: No module named 'src'` resolved in python-smoke lanes
- Cost-aware non-regression guard in `ColumnGenerationOptimizer` so baseline fallback no longer contradicts cost-prioritized candidate scoring
- Regression coverage for cost-prioritized optimizer selection in `ColumnGenerationOptimizerTests`
- README and README.ru regression status refreshed to the current validated test count (163/163)

## [1.0.0] — 2026-04-11

### Added

- Full reinforcement pipeline: isoline parsing → zone classification → rebar layout → cutting optimization → report persistence
- Clean Architecture with 4 layers: Domain, Application, Infrastructure, RevitPlugin
- DXF isoline parser with full AutoCAD ACI palette (256 colors) and ByLayer resolution
- PNG isoline parser with CIE L*a*b* ΔE*76 colour matching (ISO/CIE 11664-4)
- ML-powered image segmentation bridge via FastAPI (Python, U-Net)
- Column Generation optimizer for 1D cutting stock problem with HiGHS LP solver
- First Fit Decreasing (FFD) optimizer as baseline and fallback
- Normative engine implementing SP 63.13330.2018 (anchorage, lap splice, spacing, minimum reinforcement)
- Canonical JSON report contract (`contracts/aerobim-reinforcement-report.schema.json`)
- AeroBIM-compatible JSON export
- IFC4 export via xBIM Essentials
- CSV rebar schedule export
- Standalone CLI for running the full pipeline without Revit
- Revit plugin scaffold with compile-time `#if REVIT_SDK` guard
- Revit rebar placer with batch transaction management
- GitHub community health files: LICENSE (MIT), SECURITY.md, CONTRIBUTING.md, CODE_OF_CONDUCT.md
- GitHub Actions CI with SHA-pinned actions, CodeQL for C# and Python, dependency review
- Dependabot for NuGet, pip, and GitHub Actions
- CODEOWNERS, issue templates, and PR template
- Initial automated test suite across Domain, Application, and Infrastructure layers
- Initial Python ML smoke suite
