# Hyper-deep Red Team / Murder Board — 2026-10-03

Scope: `main` at `adca01a6530ed0c51ced82419c408a53a34df3f4`. Three independent reviews covered engineering correctness, parsers/exporters, and CI/supply-chain controls. Previously closed findings from PRs #132, #134 and #135 were not counted again.

## Immediate remediation PRs

| Priority | Finding | Remediation |
| --- | --- | --- |
| P0 | Critical verification/detailing failures still allowed Revit placement | PR #137 blocks Placement whenever any critical diagnostic exists. |
| P1 | ML client buffered unbounded responses and trusted malformed polygon cardinality | PR #136 adds response byte, zone, vertex, point-shape, coordinate, bbox, area and class limits. |
| P1 | Schedule CSV allowed delimiter/row/formula injection | PR #136 applies CSV quoting and spreadsheet neutralization. |
| P1 | NuGet vulnerabilities were informational because of `|| true` | PR #138 makes High/Critical direct and transitive findings blocking. |
| P1 | Dependency Review allowed Moderate findings and lacked a license policy | PR #138 lowers the threshold and denies AGPL/SSPL additions. |
| P1 | Dependabot omitted `OpenRebar.Cli.Tests` | PR #138 adds the missing weekly update scope. |

## Confirmed open findings

1. **Repository settings:** public API showed no tag ruleset and no configured `release` environment. Configure protected `v*` creation and required human release approval outside the codebase.
2. **OSV suppression debt:** `ml/osv-scanner.toml` contains a large blanket allowlist with shared reason/expiry. Replace it with package/version/reachability/owner/issue-specific exceptions before enabling a blocking Python vulnerability gate.
3. **SBOM relationship:** add pinned `actions/attest-sbom` so the SPDX document is machine-linked to the released ZIP rather than only co-attested as a build subject.
4. **DXF expansion DoS:** add byte/entity/depth/insert-instance/vertex budgets and checked array-product arithmetic before expanding block arrays.
5. **PNG decode budget:** call `Image.IdentifyAsync` before `Image.LoadAsync`; reduce full-frame label/visited allocations and add component budgets.
6. **Supported oblique slabs:** stop replacing the slab polygon with its bounding rectangle when support edges are declared.
7. **Oblique bar runs:** preserve exact intersections; current outward 10 mm quantization can move endpoints outside concrete.
8. **Maximum spacing:** enforce the normative spacing limit independently of provided-area verification.
9. **Effective depth:** publish per-layer depth from actual bar axes; the scalar `h - cover` is not the depth of any placed layer.
10. **ML endpoint policy:** PR #136 rejects unsafe schemes and credentials, but production deployments still need an allowlist/DNS policy and redirect validation appropriate to their network topology.

## SOTA 2026 baseline

- OpenSSF OSPS Baseline 2026
- SLSA artifact provenance and source-to-release binding
- NIST SSDF 1.2 draft: fail-safe engineering checks, vulnerability response and protected release processes
- OWASP software supply-chain and CSV/spreadsheet injection guidance

No finding is considered closed solely by documentation: closure requires executable regression evidence or an auditable repository-setting check.
