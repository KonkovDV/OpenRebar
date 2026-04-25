# ML Supply-Chain Security

**Audit Finding**: ML-1 (ML Supply-Chain Hardening)  
**Date**: 2026-04-25  
**Status**: Implemented

## Overview

The OpenRebar ML module implements reproducible, cryptographically-verified dependency and model management to prevent supply-chain attacks.

## 1. Dependency Lock File

### Using the Locked Requirements

The locked requirements file (`requirements.locked.txt`) contains all transitive dependencies pinned to exact versions with SHA256 hashes:

```bash
# Install with hash verification (prevents package substitution attacks)
python -m pip install --require-hashes -r ml/requirements.locked.txt

# Refresh the lock with the pinned pip-tools bootstrap used in CI
python -m pip install --require-hashes -r .github/requirements/pip-tools.locked.txt
python -m piptools compile --allow-unsafe --generate-hashes --output-file=ml/requirements.locked.txt ml/requirements.in
```

### Why Hash Verification?

- **Dependency Confusion**: Prevents hijacked packages from being installed (e.g., `torch` vs `torch-malicious`)
- **Reproducibility**: Ensures exact same binary is downloaded across environments
- **Audit Trail**: Hashes visible in git history for security reviews

### Generating/Updating Locks

1. Edit `ml/requirements.in` with version constraints
2. Install the pinned pip-tools bootstrap from `.github/requirements/pip-tools.locked.txt`
3. Run `python -m piptools compile --allow-unsafe --generate-hashes --output-file=ml/requirements.locked.txt ml/requirements.in`
4. Commit the new `ml/requirements.locked.txt` with a detailed commit message
5. Review all transitive dependencies added/removed

### Platform Nuance: Linux-only Torch Dependencies

PyTorch 2.11 introduces Linux-only sidecar dependencies that may not materialize when a lock is regenerated on Windows alone. To keep GitHub Actions reproducible, the Ubuntu workflows refresh `ml/requirements.locked.txt` before installation using the pinned pip-tools bootstrap in `.github/requirements/pip-tools.locked.txt`.

Operational rule:

- keep `ml/requirements.in` as the declarative source of truth,
- keep `ml/requirements.locked.txt` committed for reviewability,
- let Ubuntu CI regenerate the lock before install so Linux-only dependency hashes are present.

## 2. Model Checkpoint Verification

### MANIFEST.json

The `ml/models/MANIFEST.json` file tracks all ONNX model checkpoints:

- **SHA256 hashes**: Cryptographic fingerprints of model files
- **Metadata**: Training date, framework, performance metrics, input/output shapes
- **Provenance**: Source, license, training dataset reference
- **Verification command**: How to recompute the hash locally

The repository keeps this manifest in git even when the model registry is empty; model binaries remain release artifacts and are not committed into source control.

### Verifying Model Integrity

```bash
# Verify a downloaded model
sha256sum unet_segmentation_v1.onnx
# Compare output against MANIFEST.json entry for your model_id

# Or use openssl
openssl dgst -sha256 unet_segmentation_v1.onnx
```

### Model Deployment

1. Download model from trusted release artifact (GitHub Releases)
2. Compute SHA256 of downloaded file
3. Cross-reference against `MANIFEST.json`
4. Load model only if hash matches

Example in Python:

```python
import hashlib
import json

def load_verified_model(model_path, model_id, manifest_path="ml/models/MANIFEST.json"):
    # Read manifest
    with open(manifest_path) as f:
        manifest = json.load(f)
    
    # Compute hash
    sha256_hash = hashlib.sha256()
    with open(model_path, "rb") as f:
        for byte_block in iter(lambda: f.read(4096), b""):
            sha256_hash.update(byte_block)
    
    computed_hash = sha256_hash.hexdigest()
    
    # Verify against manifest
    model_entry = next((m for m in manifest["models"] if m["model_id"] == model_id), None)
    if model_entry is None:
      raise ValueError(f"Model {model_id} not found in manifest")
    expected_hash = model_entry["sha256"]
    if computed_hash != expected_hash:
        raise ValueError(f"Hash mismatch! {computed_hash} != {expected_hash}")
    
    # Safe to load
    import onnx
    return onnx.load(model_path)
```

## 3. CI/CD Integration

### GitHub Actions Verification

When a PR modifies ML code or dependencies:

1. **Dependency check**: Install from `requirements.locked.txt` with `--require-hashes` and verify lock formatting
2. **Manifest check**: Validate `MANIFEST.json` has valid SHA256 formats
3. **Security scan**: Check for known vulnerable packages via `pip-audit` or similar
4. **Model integrity**: (If models included in repo) Verify model hashes

Example CI step (add to `.github/workflows/ci.yml`):

```yaml
- name: Verify ML dependencies
  run: |
    python -m pip install --require-hashes -r .github/requirements/pip-tools.locked.txt
    python -m piptools compile --allow-unsafe --generate-hashes --output-file=ml/requirements.locked.txt ml/requirements.in
    python -m pip install --require-hashes -r ml/requirements.locked.txt --dry-run

- name: Validate model manifest
  run: |
    python ml/scripts/validate_model_manifest.py ml/models/MANIFEST.json
```

### Weekly Re-pinning (Optional)

To keep dependencies fresh while maintaining security:

```yaml
name: Weekly Dependency Update

on:
  schedule:
    - cron: '0 0 * * 0'  # Weekly on Sunday

jobs:
  update-deps:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Update requirements.locked.txt
        run: |
          python -m pip install --require-hashes -r .github/requirements/pip-tools.locked.txt
          python -m piptools compile --allow-unsafe --generate-hashes -U --output-file=ml/requirements.locked.txt ml/requirements.in
      
      - name: Create PR with updated dependencies
        uses: peter-evans/create-pull-request@v5
        with:
          commit-message: "chore: weekly dependency update"
          title: "Weekly dependency refresh"
          body: "Automated weekly update of Python dependencies with hash verification"
```

## 4. Development Workflow

### Adding a New Dependency

1. Add to `requirements.in` with version constraint
2. Run `python -m pip install --require-hashes -r .github/requirements/pip-tools.locked.txt`
3. Run `python -m piptools compile --allow-unsafe --generate-hashes --output-file=ml/requirements.locked.txt ml/requirements.in`
4. Review the transitive dependencies added
5. Commit both files with explanation of why dependency was added

### Adding a New Model

1. Train/obtain ONNX model
2. Compute SHA256:
   ```bash
   sha256sum unet_segmentation_v2.onnx
   ```
3. Add entry to `MANIFEST.json` with:
   - Model ID and version
   - Filename and SHA256
   - Input/output shapes
   - Training metadata
   - Performance metrics
4. Commit manifest with model as release artifact (not in git)

## 5. Security Best Practices

| Practice | Implementation |
|----------|-----------------|
| Pinned versions | All in `requirements.locked.txt` with hashes |
| Transitive lock | pinned `pip-tools` bootstrap + `python -m piptools compile` captures all dependencies |
| Hash verification | `pip install --require-hashes` enforces verification |
| Model provenance | `MANIFEST.json` tracks training date, framework, metrics |
| Audit trail | Git commit history shows all dependency changes |
| No auto-updates | Manual review required for all version bumps |
| Release artifacts | Models stored separately, not in git repository |
| CI validation | Automated checks for lock file format, manifest validity |

## 6. Troubleshooting

### "Hash mismatch" on `pip install`

**Cause**: Package was re-uploaded or cache corruption  
**Solution**:
```bash
pip cache purge
python -m pip install --require-hashes --no-cache-dir -r requirements.locked.txt
```

### `pip-compile` fails

**Cause**: Conflicting version constraints  
**Solution**:
1. Review constraints in `requirements.in`
2. Remove one constraint and try again
3. Document conflicts in code comments

### Model SHA256 mismatch

**Cause**: Corrupted download or wrong model file  
**Solution**:
```bash
# Verify download integrity
sha256sum unet_segmentation_v1.onnx

# If mismatch, re-download from official source:
# https://github.com/KonkovDV/OpenRebar/releases/download/models-v1.0.0/unet_segmentation_v1.onnx
```

## References

- **Audit Finding**: ML-1 (ML Supply-Chain Hardening)
- **pip-tools**: https://pip-tools.readthedocs.io/
- **Python Security**: https://peps.python.org/pep-0665/ (Hash verification)
- **ONNX Spec**: https://github.com/onnx/onnx/blob/main/docs/IR.md
- **OWASP**: Supply Chain Security https://owasp.org/www-project-dependency-check/

---

**Custodian**: OpenRebar ML Team  
**Last Updated**: 2026-04-25  
**Review Interval**: Quarterly
