# Plan: README stage and supply chain — 2026-10-14

## Context

PR #210 set the product boundary. Two gaps remain against the other
KonkovDV READMEs. AeroBIM and SynAPS-GridPlan separate a laboratory fixture
from a pilot, and they say which document wins if the README drifts. The
OpenRebar status line also links to a heading that does not exist.

## Changes

- Point the status line at the real quick-start heading.
- Add a stage section: synthetic examples, what they show, what they do not.
  Do not copy a TRL number from another repository.
- Add the supply-chain facts already in CI: locked NuGet, hashed ML lock,
  Scorecard, SBOM, attestation, and the ImageSharp 3.1.12 hold.
- Replace the one-paragraph neighbour note with a three-row table: calculation
  program, OpenRebar, AeroBIM.

## Acceptance

Both READMEs stay paired. The regression sentence stays 453/453.
`verify_readme_regression_claim.py` passes.
