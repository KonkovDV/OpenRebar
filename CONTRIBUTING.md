# Contributing

## Working Principles

- Keep the dependency direction strict: Domain -> nothing, Application -> Domain,
  Infrastructure -> Domain + Application, RevitPlugin -> all outer wiring.
- Do not introduce Revit SDK dependencies outside `src/OpenRebar.RevitPlugin/`.
- Keep all internal numeric geometry in millimetres. Unit conversion belongs only
  at the Revit boundary.
- Prefer small, reviewable commits with focused diffs.

## Local Setup

### .NET

The restore, build, format, and five test commands are in the root README quick start. `dotnet test OpenRebar.sln` is not that suite: the CLI tests are .NET 10 only.

### Python ML module

```bash
cd ml
python -m pip install --require-hashes -r requirements.locked.txt
python -m pytest tests -q
```

## What To Validate Before Opening A PR

1. The restore, build, format, and five test commands in the root README quick start
2. `python tools/ci/verify_readme_regression_claim.py` after those tests, if the README count might have moved
3. If you touched `ml/`, run `pytest tests -q` from `ml/`
4. If you changed ML dependency or workflow surfaces, run the relevant steps from [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md)
5. If you changed docs, re-read the root README and the pages you touched
6. Do not include local logs, generated temp files, ML checkpoints, or Revit SDK binaries

## Documentation Contributions

- Keep `README.md` and `README.ru.md` saying the same thing.
- When the test count or a CI command changes, update both pages in the same pull request.
- Link to a page instead of pasting it twice.

## Architecture Guardrails

- Ports live in `src/OpenRebar.Domain/Ports/`
- Adapters live in `src/OpenRebar.Infrastructure/`
- Use cases live in `src/OpenRebar.Application/UseCases/`
- Services are registered in `src/OpenRebar.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`. The CLI and `src/OpenRebar.RevitPlugin/Bootstrap.cs` compose them
- New I/O paths must be introduced as ports first, adapters second

## Pull Requests

- Explain the problem, not only the code delta
- Link changed contracts, docs, or audit notes when behavior changes
- Add or update tests for bug fixes and new behavior
- Keep publication-facing docs accurate: `README.md`, `docs/architecture.md`,
  `docs/README.md`. April 2026 audits in `docs/archive/` are not the source of truth.

## Security

If your change affects workflows, external HTTP calls, report export, dependency
updates, or credentials handling, review `SECURITY.md` before opening the PR.
