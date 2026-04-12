# Security Policy

## Supported Scope

This repository is prepared for public GitHub publication, but some security controls
must still be enabled in GitHub settings after the first push.

Current support expectations:

| Surface | Status |
| --- | --- |
| `main` branch before first public release | Best effort |
| Latest public tag after release process exists | Supported |
| Older unpublished branches and local forks | Not guaranteed |

## Reporting A Vulnerability

1. Do not open a public issue for a suspected vulnerability.
2. If GitHub private vulnerability reporting is enabled for the repository, use the
   Security tab and submit a private report there.
3. If private reporting is not enabled yet, contact the maintainer privately before
   any public disclosure.
4. Include the affected commit or branch, reproduction steps, impact, and whether
   secrets or customer data may be exposed.

## In Scope

- GitHub Actions workflows and supply-chain configuration
- Secret exposure in repository history or workflow logs
- DXF / PNG parsing boundaries and report export paths
- ML HTTP integration boundary (`ml/` and `HttpImageSegmentationService`)
- Contracts in `contracts/` and machine-readable report artifacts

## Out Of Scope

- Local Autodesk Revit installations and proprietary project models not stored here
- Vulnerabilities introduced only in downstream private forks without reproduction
- Social engineering, phishing, or attacks that do not involve this repository

## Disclosure Expectations

- Initial acknowledgement target: 5 business days
- High / critical issues: triage immediately after confirmation
- Coordinated disclosure is expected until a fix or mitigation is available

## GitHub-Side Controls To Enable After Publication

- Private vulnerability reporting
- Secret scanning and push protection
- Code scanning / CodeQL
- Branch rulesets with pull-request review requirements
