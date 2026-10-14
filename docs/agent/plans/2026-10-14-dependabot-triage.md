# Plan: Dependabot triage — 2026-10-14

## Context

r10 D-1. Twenty-five Dependabot pull requests (#160–#184), all opened on
2026-10-11, fail `build-and-test`. The NuGet bumps fail restore with `NU1004`
(the lock file frameworks do not match the project on that branch) and with
`NU1902`/`NU1903` for ImageSharp 3.1.12, because those branches do not contain
the audit suppressions from PR #185. The Actions bumps fail on the same
ImageSharp restore error. No pull request reached the tests, so there is no
TRX count for xunit 4.0.1. None of them is safe to merge.

## Changes

- One NuGet `directories` entry covers every project. One package bump then
  opens one pull request instead of one per project.
- Ignore ImageSharp `>= 3.2.0`. Version 4 needs a license key (Q-5). Version
  3.2.0 does not patch the five accepted advisories; those are fixed only in
  4.1.2, and the audit exception is pinned to 3.1.12. A 3.1 patch can still
  arrive.
- Ignore FluentAssertions `>= 8.0.0` on that same entry.
- Do not ignore xunit. The test count was never measured.

## Acceptance

The configuration pull request is green on GitHub. Pull requests #160–#184 are
closed with a one-line reason. `main` is the only branch left, and its CI is
green. Fresh Dependabot pull requests, if any, are reviewed later, one at a
time.
