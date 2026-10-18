# Plan: attest the SBOM against the CLI archive — 2026-10-18

## Context

r10 §4.15 part 2. The release already generates an SPDX file and attests
the ZIP, the SBOM, and the checksum file as three build subjects. That
does not record that the SBOM describes the ZIP.

## Changes

- Pin `actions/attest-sbom` at `v4.1.0`
  (`c604332985a26aa8cf1bdc465b92731239ec6b9e`).
- Subject is the CLI ZIP. `sbom-path` is `OpenRebar-sbom.spdx.json`.
- The publish job already has `id-token: write` and `attestations: write`.
- Tag `v4.1.0` of that action prints a deprecation warning and calls
  `actions/attest`. The inputs stay `subject-path` and `sbom-path`.

## Acceptance

`lint-workflows` accepts the pin. No release is cut from this change.
