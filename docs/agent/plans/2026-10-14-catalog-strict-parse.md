# Plan: strict supplier catalogue — 2026-10-14

## Context

r10 PR-12, and the SOTA 2026 near-term row of the same name. A catalogue is an
input to cutting. The CSV loader parsed with the current culture and skipped a
bad row, so a comma decimal or a zero length disappeared instead of failing.

## Changes

- CSV header is `LengthMm,PricePerTon,InStock`, case-insensitive.
- Numbers use `CultureInfo.InvariantCulture`. An empty price is allowed. A
  length must be finite and greater than 0. A price must be empty or finite
  and at least 0. `InStock` is `true` or `false`. A duplicate length fails.
- A bad CSV row throws `SupplierCatalogLoadException` and names the line.
- JSON uses the same value rules.
- Examples do not change. No schema change.

## Acceptance

New tests live in `FileSupplierCatalogLoaderTests`. The counted suite moves
from 423 by the number of new facts and inline rows. CI on GitHub is green.
