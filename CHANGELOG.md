# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **OpenRebar rebrand**: full namespace, project, and folder rename from the legacy project name to OpenRebar
- **P1 Revit boundary**: host floor structural validation (category, compound structure, min thickness)
- **P1 Revit boundary**: rebar tag creation pass with `IndependentTag.Create` and midpoint positioning
- **P1 Revit boundary**: bending detail tracking per unique `RebarShape`
- **P3 ML training pipeline**: `ml/src/training/` module with dataset loader, augmentation, train loop
- **P3 ML evaluation**: per-class IoU, mean IoU, pixel accuracy, confusion matrix
- **P3 ONNX export**: `export_onnx.py` for CPU-only C# inference via OnnxRuntime
- **P3 Benchmarks**: inference latency, parameter count, batch throughput, ONNX exportability tests
- **Academic geometry hardening**: complex-zone decomposition now persists coverage and over-coverage metrics
- **Academic optimization TEVV**: exact small-instance bar-count cross-checks for the column-generation optimizer
- **Academic optimization TEVV**: benchmark pack for score-gap and waste-gap distribution on small CSP instances
- **Optimization hardening**: exact discrete search path for tiny mixed-stock instances
- **Canonical report provenance**: normative profile + geometry/optimization provenance in `*.result.json`
- CLI arguments `--slab-width` and `--slab-height` for configurable slab footprint
- CLI boundary validation for numeric arguments (thickness, cover, slab dimensions)
- CLI integration tests verifying exported artifacts and custom geometry
- CHANGELOG.md with Keep a Changelog format
- Release workflow with SBOM generation and artifact attestation
- JSON report schema validation test against `contracts/aerobim-reinforcement-report.schema.json`

### Changed

- CLI no longer uses a hardcoded 30×20m demo slab footprint; geometry is parameterized
- CLI wraps pipeline execution in structured error handling with meaningful exit codes

### Fixed

- Help text alignment for `--legend` option

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
- 114 automated tests across Domain, Application, and Infrastructure layers
- 4 Python ML smoke tests
