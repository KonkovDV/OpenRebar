# Plan: ML endpoint allowlist — 2026-10-18

## Context

r10 §4.15 part 3. The segmentation client already rejects a non-HTTP
scheme and a URL with user info. It still follows a redirect, including
one to another host. The OSV list and the SBOM attestation stay for
later: the first waits on the ML lock, the second is a release workflow.

## Changes

- The default allowlist is loopback: `localhost`, `127.0.0.1`, `::1`.
- A caller can add hosts. The CLI adds the host of `--ml-url`.
- The owned HTTP handler does not follow redirects. A 3xx response,
  including one whose location is another host, fails the call.
- simple-slab does not call the service. No golden change.

## Acceptance

A host outside the list is rejected at construction. A redirect to
another host is rejected and that host is not requested.
