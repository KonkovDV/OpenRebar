# Batch Benchmark Corpus

This directory is the default discovery root for optional production slab-batch benchmark fixtures used by `BatchReinforcementCorpusFixtureTests`.

The test looks for one of these sources:

1. `OPENREBAR_BATCH_CORPUS_ROOT` environment variable pointing to a directory with `manifest.json`
2. This directory, when `manifest.json` is present here

When no manifest is found, the corpus test exits without failing so the normal CI lane stays green.

## Expected Files

- `manifest.json` — real corpus manifest used by the test
- `*.dxf` — slab isoline inputs referenced by the manifest
- optional subdirectories — any layout is fine as long as `dxfPath` in the manifest resolves correctly

## Bootstrap

1. Copy `manifest.example.json` to `manifest.json`.
2. Place the referenced DXF files under this directory or point `dxfPath` to subfolders.
3. Run:

```bash
dotnet test tests/OpenRebar.Application.Tests/OpenRebar.Application.Tests.csproj --filter Category=Corpus
```

## Manifest Contract

Each case defines enough boundary data to run the end-to-end application pipeline against a real DXF fixture:

- `name` — human-readable case identifier
- `slabId` — stable slab/result identifier
- `dxfPath` — path to a DXF file, relative to the corpus root
- `slabWidthMm`, `slabHeightMm` — slab boundary used by the benchmark harness
- optional `thicknessMm`, `coverMm`, `concreteClass`, `levelName`
- optional `legendEntries` — color-to-spec mapping for the DXF entities
- optional `maxBarRegression`, `maxWasteRegressionPercent`, `maxAbsoluteWastePercent`

The benchmark compares each case against the shipped `FirstFitDecreasingOptimizer` baseline and also verifies that canonical `*.result.json` reports are persisted successfully.