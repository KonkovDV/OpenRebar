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

```bash
dotnet restore OpenRebar.sln
dotnet build OpenRebar.sln
dotnet test OpenRebar.sln
```

### Python ML module

```bash
cd ml
python -m pip install -r requirements.txt
pytest tests -q
```

## What To Validate Before Opening A PR

1. `dotnet build OpenRebar.sln`
2. `dotnet test OpenRebar.sln`
3. If you touched `ml/`, run `pytest tests -q` from `ml/`
4. If you changed docs or governance surfaces, re-read them for public GitHub safety
5. Do not include local logs, generated temp files, ML checkpoints, or Revit SDK binaries

## Architecture Guardrails

- Ports live in `src/OpenRebar.Domain/Ports/`
- Adapters live in `src/OpenRebar.Infrastructure/`
- Use cases live in `src/OpenRebar.Application/UseCases/`
- Composition root stays in `src/OpenRebar.RevitPlugin/Bootstrap.cs`
- New I/O paths must be introduced as ports first, adapters second

## Pull Requests

- Explain the problem, not only the code delta
- Link changed contracts, docs, or audit notes when behavior changes
- Add or update tests for bug fixes and new behavior
- Keep publication-facing docs accurate: `README.md`, `docs/architecture.md`,
  `HYPER_DEEP_AUDIT_REPORT.md`, `TASKS.md`

## Security

If your change affects workflows, external HTTP calls, report export, dependency
updates, or credentials handling, review `SECURITY.md` before opening the PR.
