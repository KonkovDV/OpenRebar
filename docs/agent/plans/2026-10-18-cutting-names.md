# Plan: cutting names and stack hash — 2026-10-18

## Context

`FirstFitDecreasingOptimizer` keeps the open bar with the smallest remainder.
That is best fit. r10 §4.12 renames the class and its id. `MinScrapLengthMm`
is unused and comes off the settings. A report stores `stackTraceSha256`
instead of the stack text. Schema stays 2.1.0.

## Changes

- Rename the class to `BestFitDecreasingOptimizer`. Id
  `best-fit-decreasing-v1`. Its integerization label is `best-fit-decreasing`.
  It is not registered in DI. Column generation still constructs it as the
  quality floor.
- The emergency packer in the pipeline still fills the first bar that fits.
  Its label stays `first-fit-decreasing`. Renaming that label would describe
  a different algorithm. The cuts stay the same.
- Delete `MinScrapLengthMm`. Do not add a scrap threshold.
- Hash a diagnostic stack with SHA-256 when diagnostics are on, and log the
  text. Otherwise the field is null.

## Golden

simple-slab uses `column-generation-relaxation-v2`. That provenance does not
change. The only key change is `stackTrace: null` to `stackTraceSha256: null`.
Lengths and masses stay.
