# Branch Protection Checklist

The owner applies these rules in **Settings → Rules → Rulesets**. A ruleset can cover `main` and the `v*` tags in one place.

## `main`

1. Require a pull request before merging.
2. Require these status checks to pass: `lint-workflows`, `build-and-test`, `python-smoke`, `corpus-note`, `dependency-review`, `CodeQL (C#)`, `CodeQL (Python)`.
3. Require the branch to be up to date before merging.
4. Block force pushes and deletion.
5. With one maintainer, a required approving review blocks every merge. Add it when a second reviewer exists.

`ifc-validation` runs only when the exporter or the examples change, and `benchmark-corpus` runs on a schedule. Neither belongs in the required list.

## `v*` tags

1. Only the release role creates a tag.
2. Block updates and deletion.
3. A tag points at a commit on `main`. The release workflow checks this again.

## Already on

Secret scanning, push protection, and private vulnerability reporting.
