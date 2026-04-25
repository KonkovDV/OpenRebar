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

## Publication Guardrails

- no secrets or proprietary assets in release artifacts;
- benchmark or quality claims must reference concrete artifacts;
- roadmap statements must remain separate from delivered claims.
