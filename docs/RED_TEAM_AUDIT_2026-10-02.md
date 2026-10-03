# Red Team / Murder Board audit — 2026-10-02

Scope: `main` at `45b63ccc60fe97aec7137c96f782caa780001ca6`. The review covered engineering fail-safety, .NET/Python boundaries, CI/CD and supply-chain controls. Findings were triaged against the OpenSSF OSPS Baseline (2026-02-19), SLSA, NIST SSDF 1.2 draft, and OWASP Software Supply Chain Security guidance.

## Fixed in this PR

| Severity | Finding | Resolution |
| --- | --- | --- |
| Critical | Polygon decomposition thresholds were configured but never enforced | The pipeline now records warning diagnostics and aborts before calculation, optimisation and placement when strict mode is enabled. Missing/non-finite metrics fail the gate. |
| Critical | A positive-area zone with no grid sample returned `Passed` | Verification now fails closed when no cell can be sampled from a non-empty zone. |
| High | `NaN`/infinity crossed engineering boundaries | Slab dimensions and legend intervals reject non-finite values; a non-finite additional-area request requires human review. |

Regression tests cover warning-only and critical decomposition modes, a 20 mm strip on a 50 mm verification grid, and non-finite domain inputs.

## Open P0/P1 backlog

1. Protect `main` with a ruleset: PR-only changes, required CODEOWNER review, strict required checks, no force-push/deletion, narrow audited bypass.
2. Protect `v*` tags and bind releases to a reviewed `main` commit and an approved release environment.
3. Build once, test the same immutable artifacts, and attach the CLI bundle, SBOM, checksums and provenance to the release.
4. Make NuGet and Python vulnerability audits blocking; replace blanket OSV exceptions with package/version/owner/issue/expiry records.
5. Verify the actual model-file SHA-256 against `ml/models/MANIFEST.json` before model loading.
6. Treat maximum bar-spacing violations as normative failures independent of area verification.
7. Preserve exact host intersections for oblique boundaries; quantize fabrication lengths, not geometry endpoints.
8. Define effective depth per reinforcement layer/diameter instead of `h - cover`.
9. Add image pixel/decompression limits, bounded inference concurrency and timeouts to the ML service.
10. Add a pure placement-plan test boundary for Revit units, transforms, transaction rollback and idempotency.

## Reference baseline

- OpenSSF OSPS Baseline 2026: https://baseline.openssf.org/versions/2026-02-19.html
- SLSA: https://slsa.dev/
- NIST SSDF: https://csrc.nist.gov/projects/ssdf
- OWASP Software Supply Chain Security Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/Software_Supply_Chain_Security_Cheat_Sheet.html
