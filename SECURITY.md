# Security Policy

## Report a vulnerability

Report it privately on GitHub: **Security → Report a vulnerability**, or open [a new advisory](https://github.com/KonkovDV/OpenRebar/security/advisories/new) directly. Please do not open a public issue for it.

Include the commit, the steps to reproduce, the impact, and whether secrets or project data can leak.

## What is supported

| Surface | Support |
| --- | --- |
| `main` | Fixes land here first |
| A `v*` release tag | Supported once tags exist; none has been cut yet |
| Forks and old branches | Not covered |

## In scope

- GitHub Actions workflows and the supply chain: locked NuGet restore, the hashed ML lock, the release SBOM and its attestation
- Secrets in history or in workflow logs
- DXF and PNG parsing, including the 25-million-pixel frame limit
- The ML HTTP client: loopback by default, the named `--ml-url` host, no redirect to another host
- The report contracts in `contracts/` and every file a run writes

## Out of scope

- A local Revit installation and project models that are not in this repository
- A flaw that exists only in a private fork and cannot be reproduced here
- Social engineering and phishing

## What happens next

- An acknowledgement within 5 business days
- A high or critical finding is triaged as soon as it is confirmed
- Disclosure is coordinated until a fix or a mitigation is out

## Controls on this repository

| Control | State |
| --- | --- |
| Private vulnerability reporting | On |
| Secret scanning and push protection | On |
| CodeQL for C# and Python | Every push, pull request, and a weekly run |
| OpenSSF Scorecard | On `main` |
| Dependency review | Every pull request; a moderate finding or an AGPL or SSPL license fails it |
| Branch rules on `main` and `v*` tags | Set by the owner; see [.github/BRANCH_PROTECTION_CHECKLIST.md](.github/BRANCH_PROTECTION_CHECKLIST.md) |
