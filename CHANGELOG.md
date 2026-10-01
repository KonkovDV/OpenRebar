# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Report `stages[]` with stable `snake_case` reason codes, and `boundStatus` (`Proven` or `NotProven`) on each cutting result. A dual bound is omitted until column generation has converged.
- PNG calibration (`--px-per-mm`, `--origin-px`, `--roi-px`) and a closed-polyline DXF reader. The simple-slab PNG twin matches the DXF zone.
- CLI exit codes: 0 passed, 1 input or IO, 2 verification failed, 3 partial result. Stack traces are written only with `--include-diagnostics`.
- CI installs the committed Python lockfile. A missing batch-corpus manifest fails the `benchmark-corpus` job. IFC example validation runs nightly and on export pull requests.
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
- Anchorage is part of the bar geometry and stops at the working area. `Start` and `End` are the placed ends, and the schedule, the cutting list, and the Revit line all use that length. An end that cannot take the full anchorage is marked from the profile (`NeedsHook` in `generic`) and the report carries a warning. The area check then starts its development ramp at that end, so a bar stopped on the slab edge can fail the check.
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
- Comprehensive multi-level audit report `docs/COMPREHENSIVE_PROJECT_AUDIT_2026_04_25.md` with evidence-backed findings and remediation log

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
