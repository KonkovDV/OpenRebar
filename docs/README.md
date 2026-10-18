# Documents

The product page is [README.md](../README.md) ([Russian](../README.ru.md)). This folder is the rest of the writing.

## Where to look

| Question | File |
|---|---|
| How the program is split | [architecture.md](architecture.md) |
| Which clause is which method | [NORMATIVE_TRACEABILITY.md](NORMATIVE_TRACEABILITY.md) |
| Which command backs a public number | [VALIDATION_BASELINE.md](VALIDATION_BASELINE.md) |
| What is still to do | [OPENREBAR_AGENT_PLAN_r10_2026_10_14.md](OPENREBAR_AGENT_PLAN_r10_2026_10_14.md) |
| What the 2026 papers do that this repository does not | [SOTA_2026.md](SOTA_2026.md) |
| What changed | [CHANGELOG.md](../CHANGELOG.md) |

`archive/` and the older plan files are history. The code on `main` wins if a page and the tests disagree.

## How to check a claim

The counted suite is the five `dotnet test` commands in the README quick start. `dotnet test OpenRebar.sln` is not that suite: the CLI project is .NET 10 only, and a solution-wide run does not write the `test-results.trx` files the count script sums.

A lock refresh, from `ml/`, uses the same platforms as the header of `requirements.locked.txt`:

```bash
python -m pip install --require-hashes -r ..\.github\requirements\pip-tools.locked.txt
python -m piptools compile --allow-unsafe --generate-hashes --newline lf --output-file=requirements.locked.txt --pip-args "--python-version 3.11 --platform manylinux_2_28_x86_64 --platform manylinux_2_17_x86_64 --platform manylinux2014_x86_64 --implementation cp --abi cp311 --only-binary=:all:" requirements.in
```

The lock is CPU torch `2.13.0+cpu`. numpy stays on 1.26, whose wheels are tagged `manylinux_2_17`, so the refresh names that platform as well as `manylinux_2_28`. CI installs the committed lock. It does not rewrite it.
