# AGENTS.md

Source of truth: the code and tests on `main`, then [r8](docs/OPENREBAR_AGENT_PLAN_r8_2026_10_02.md), then [r7](docs/OPENREBAR_AGENT_PLAN_r7_2026_10_02.md), then [r6](docs/OPENREBAR_AGENT_PLAN_r6_2026_10_01.md), then [r5](docs/OPENREBAR_AGENT_PLAN_2026_10_01.md). Other files under `docs/` are history. A human answer recorded in [docs/agent/decisions/DECISIONS.md](docs/agent/decisions/DECISIONS.md) overrides the plan.

Do not commit or push unless asked. Do not force-push.

## CI commands

These lines are copied from `.github/workflows/ci.yml`. `tools/ci/verify_agents_md.py` fails if one is missing here. A single-line `run:` value that contains `: ` is not a YAML string; put it in a block scalar. Job `lint-workflows` runs actionlint 1.7.12. `dotnet test OpenRebar.sln -f net8.0` is not a CI command: `OpenRebar.Cli` and `OpenRebar.Cli.Tests` are `net10.0` only.

```text
dotnet restore OpenRebar.sln --locked-mode -p:EnableWindowsTargeting=true -p:Platform=x64
dotnet build OpenRebar.sln --no-restore --configuration Release -p:EnableWindowsTargeting=true -p:Platform=x64
dotnet format OpenRebar.sln --verify-no-changes --no-restore
dotnet test tests/OpenRebar.Domain.Tests/OpenRebar.Domain.Tests.csproj --no-build --configuration Release -f net8.0 --verbosity normal --logger "trx;LogFileName=test-results.trx"
dotnet test tests/OpenRebar.Infrastructure.Tests/OpenRebar.Infrastructure.Tests.csproj --no-build --configuration Release -f net8.0 --verbosity normal --logger "trx;LogFileName=test-results.trx"
dotnet test tests/OpenRebar.TestCorpus/OpenRebar.TestCorpus.csproj --no-build --configuration Release -f net8.0 --verbosity normal --logger "trx;LogFileName=test-results.trx"
dotnet test tests/OpenRebar.Application.Tests/OpenRebar.Application.Tests.csproj --no-build --configuration Release -f net8.0 --verbosity normal --logger "trx;LogFileName=test-results.trx"
dotnet test tests/OpenRebar.Cli.Tests/OpenRebar.Cli.Tests.csproj --no-build --configuration Release -f net10.0 --verbosity normal --logger "trx;LogFileName=test-results.trx"
python tools/ci/verify_readme_regression_claim.py
python tools/ci/verify_agents_md.py
dotnet test tests/OpenRebar.Domain.Tests/OpenRebar.Domain.Tests.csproj --no-build --configuration Release -f net10.0 --logger "trx;LogFileName=tfm-net10-results.trx"
dotnet test tests/OpenRebar.Infrastructure.Tests/OpenRebar.Infrastructure.Tests.csproj --no-build --configuration Release -f net10.0 --logger "trx;LogFileName=tfm-net10-results.trx"
dotnet test tests/OpenRebar.TestCorpus/OpenRebar.TestCorpus.csproj --no-build --configuration Release -f net10.0 --logger "trx;LogFileName=tfm-net10-results.trx"
dotnet test tests/OpenRebar.Application.Tests/OpenRebar.Application.Tests.csproj --no-build --configuration Release -f net10.0 --logger "trx;LogFileName=tfm-net10-results.trx"
OPENREBAR_TFM_HASH_DIR="$PWD/artifacts/tfm/net8" dotnet test tests/OpenRebar.Infrastructure.Tests/OpenRebar.Infrastructure.Tests.csproj --no-build --configuration Release -f net8.0 --filter FullyQualifiedName~Schedule_HashFile
OPENREBAR_TFM_HASH_DIR="$PWD/artifacts/tfm/net8" dotnet test tests/OpenRebar.TestCorpus/OpenRebar.TestCorpus.csproj --no-build --configuration Release -f net8.0 --filter FullyQualifiedName~CorpusA0_HashFile
OPENREBAR_TFM_HASH_DIR="$PWD/artifacts/tfm/net8" dotnet test tests/OpenRebar.Application.Tests/OpenRebar.Application.Tests.csproj --no-build --configuration Release -f net8.0 --filter FullyQualifiedName~Examples_WriteNormalizedArtifactHashes
python tools/ci/compare_tfm_hashes.py artifacts/tfm/net8 artifacts/tfm/net10
dotnet list OpenRebar.sln package --include-transitive --vulnerable > artifacts/dependency-audit/deps-vulnerable.txt || true
dotnet list OpenRebar.sln package --outdated > artifacts/dependency-audit/deps-outdated.txt || true
dotnet test tests/OpenRebar.Infrastructure.Tests/OpenRebar.Infrastructure.Tests.csproj --no-build --configuration Release -f net8.0 --filter FullyQualifiedName~ColumnGenerationBenchmarkPackTests --logger "trx;LogFileName=benchmark-results.trx"
dotnet publish src/OpenRebar.Cli/OpenRebar.Cli.csproj --no-build --configuration Release --output ./publish/cli
python -m pip install --require-hashes -r ml/requirements.locked.txt
python ml/scripts/validate_model_manifest.py ml/models/MANIFEST.json
pytest tests -q
dotnet test tests/OpenRebar.Application.Tests/OpenRebar.Application.Tests.csproj --no-build --configuration Release -f net10.0 --filter FullyQualifiedName~BatchReinforcementCorpusFixtureTests
```

A project-scoped `--no-build` test does not take `-p:Platform=x64`. The solution build already writes `bin/Release`.

## Invariants

From r5 §5.1–5.4:

- Domain has no NuGet packages. NetTopologySuite, HiGHS, xBIM, and ImageSharp stay in Infrastructure, behind ports in `src/OpenRebar.Domain/Ports/`.
- The Revit API is referenced only in `src/OpenRebar.RevitPlugin/` under `#if REVIT_SDK`.
- Dependency injection is composed in `ServiceCollectionExtensions` and `Bootstrap.cs`.
- A new field on `*.result.json` needs a schema bump. The schema rejects unknown properties.
- Check status is `Passed`, `Failed`, or `NotEvaluated`. Crack width and deflection stay `NotEvaluated` when the input is only required area.
- Explain every changed figure in `examples/*/expected` in the PR. An unexplained golden diff blocks the change.
- Update the test count in `README.md` and `README.ru.md` together.

## Stop

Open questions are r7 §7. Where r6 still says otherwise, r7 wins: hook credit stays 0, Rs is the table already in the repo, and Revit builds are `RevitNet8` / `RevitNet10` by runtime. Do not invent a norm, a lap-force rule, or a U-bar leg. The official Amendment 1 order text and the primary text of clauses 10.3.25 and 10.4.9 were not opened. Safe defaults until a person answers: `NotEvaluated: transverseInLap`, no generated U-bars, first pilot is an auditor (Q-12).

Before code, write `docs/agent/plans/YYYY-MM-DD-<task>.md`.
