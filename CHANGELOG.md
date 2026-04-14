# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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

### Changed

- CLI no longer uses a hardcoded 30×20m demo slab footprint; geometry is parameterized
- CLI wraps pipeline execution in structured error handling with meaningful exit codes
- Validation story for cutting quality now distinguishes shipped generated/fixture-driven batch harnesses from still-missing production slab corpora

### Fixed

- Help text alignment for `--legend` option
- Windows Unicode path handling for ML image loading (`cv2.imread` replaced with Unicode-safe decode path in training and inference)
- Missing ONNX export dependencies in `ml/requirements.txt` (`onnx`, `onnxscript`) for `torch.onnx.export`
- `torch.export` ONNX dynamic-shape wiring now uses positional tuple specs compatible with PyTorch 2.11 single-input exports
- ONNX export default opset raised to `18`, matching the PyTorch 2.11 exporter implementation floor and avoiding downgrade-conversion warnings

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
