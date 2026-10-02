# Release Policy

## Scope

This policy defines release-readiness for OpenRebar.

## Versioning

- SemVer tags: vMAJOR.MINOR.PATCH
- MAJOR for breaking report/API contract changes
- MINOR for backward-compatible features
- PATCH for fixes and hardening

## Pre-Release Baseline

Before creating a release tag:

1. dotnet restore/build/test pass in Release configuration;
2. Python smoke lane passes with hash-locked dependencies;
3. release workflow produces expected artifacts and SBOM;
4. README/docs claim boundaries match delivered behavior;
5. no unresolved critical security findings.

## Evidence in Release Notes

Include:

- commit range;
- validation commands and outcomes;
- contract/schema impact;
- explicit known limitations.

## Target frameworks

The core libraries (`Domain`, `Application`, `Infrastructure`) build for `net8.0` and `net10.0`. The CLI ships on `net10.0`. Revit is two builds chosen by the process runtime, not by the product year:

- `RevitNet8` (`net8.0-windows`) when `Environment.Version.Major` is below 10. That is Revit 2025/2026 that has not taken the .NET 10 update.
- `RevitNet10` (`net10.0-windows`) when the major version is 10 or higher. That is Revit 2027 and Revit 2025/2026 after the runtime update.

Install the matching folder under `src/OpenRebar.RevitPlugin/addin/` next to that build's `OpenRebar.RevitPlugin.dll`. A `.addin` file cannot branch on the runtime, and this repository does not reference the Revit API, so there is no single loader assembly.

After 10 November 2026, `net8.0` is supported only for Revit that has not received the .NET 10 update. Review that exception on 1 June 2027. The CLI is not part of the exception: it stays on `net10.0`.

The longer life of the `net8.0-windows` build for Russian installs is a conclusion, not a quote from Autodesk. Autodesk stopped selling its products to Russian companies on 20 March 2024, so those installs are unlikely to receive the runtime update.

Quotes, accessed 2026-10-02:

- Mikako Harada, 29 April 2026, [Call for Preview Testing: Revit 2026/2025 Migration to .NET 10](https://blog.autodesk.io/call-for-preview-testing-revit-2026-2025-migration-to-net-10/): Revit 2025 and 2026 are built on .NET 8; Microsoft ends .NET 8 support on 10 November 2026; Autodesk plans to migrate those two Revit versions to .NET 10. Most add-ins keep working. Complex native or third-party cases may need recompilation.
- Madhukar Moogala, 24 July 2026, [Autodesk Desktop Products 2025/2026: .NET 10 Updates](https://blog.autodesk.io/autodesk-desktop-products-2025-2026-net-10-updates/): "There are no API changes in any of these products as part of this update. The only change is that the underlying .NET runtime is moving from .NET 8 to .NET 10." Revit 2026.5 was planned for the first week of August 2026, and the 2025 line for the second week of September 2026. An add-in hit by a runtime breaking change must be recompiled for .NET 10.

Shared code stays on APIs that exist in `net8.0`. One `net10.0` DLL is not the add-in for every Revit.

## Publication Guardrails

- no secrets or proprietary assets in release artifacts;
- benchmark or quality claims must reference concrete artifacts;
- roadmap statements must remain separate from delivered claims.
