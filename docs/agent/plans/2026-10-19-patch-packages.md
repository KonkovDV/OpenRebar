# Plan: patch the 10.0 Microsoft packages — 2026-10-19

## Context

Dependabot opened eight NuGet pull requests. Restore fails with NU1004: the rewritten lock file does not list the same target frameworks as the project. Two requests are green: the CodeQL upload-sarif pin, and FluentAssertions in the CLI tests only.

HiGHS and xBIM stay. A native solver or an IFC library can change the golden files, and this step does not explain a golden diff.

## Changes

- Microsoft.Extensions.* 10.0.12 to 10.0.13, and System.Text.Json 10.0.7 to 10.0.13.
- FluentAssertions 7.1.0 to 7.2.2 in every test project. Stay on 7.x.
- Regenerate the lock files with both target frameworks still present.

## Acceptance

`dotnet restore OpenRebar.sln --locked-mode` succeeds. No golden figure changes.
