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
pip install --require-hashes -r ml/requirements.locked.txt

# Or use pip-tools for updates
pip-compile --generate-hashes -o requirements.locked.txt requirements.in
```

### Why Hash Verification?

- **Dependency Confusion**: Prevents hijacked packages from being installed (e.g., `torch` vs `torch-malicious`)
- **Reproducibility**: Ensures exact same binary is downloaded across environments
- **Audit Trail**: Hashes visible in git history for security reviews

### Generating/Updating Locks

1. Edit `requirements.in` with version constraints
2. Run `pip-compile --generate-hashes -o requirements.locked.txt requirements.in`
3. Commit the new `requirements.locked.txt` with a detailed commit message
4. Review all transitive dependencies added/removed

## 2. Model Checkpoint Verification

### MANIFEST.json

The `ml/models/MANIFEST.json` file tracks all ONNX model checkpoints:

- **SHA256 hashes**: Cryptographic fingerprints of model files
- **Metadata**: Training date, framework, performance metrics, input/output shapes
- **Provenance**: Source, license, training dataset reference
- **Verification command**: How to recompute the hash locally

### Verifying Model Integrity

```bash
# Verify a downloaded model
sha256sum unet_segmentation_v1.onnx
# Compare output against MANIFEST.json["models"][0]["sha256"]

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

def load_verified_model(model_path, manifest_path="ml/models/MANIFEST.json"):
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
    expected_hash = manifest["models"][0]["sha256"]
    if computed_hash != expected_hash:
        raise ValueError(f"Hash mismatch! {computed_hash} != {expected_hash}")
    
    # Safe to load
    import onnx
    return onnx.load(model_path)
```

## 3. CI/CD Integration

### GitHub Actions Verification

When a PR modifies ML code or dependencies:

1. **Dependency check**: Verify `requirements.locked.txt` is properly formatted with hashes
2. **Manifest check**: Validate `MANIFEST.json` has valid SHA256 formats
3. **Security scan**: Check for known vulnerable packages via `pip-audit` or similar
4. **Model integrity**: (If models included in repo) Verify model hashes

Example CI step (add to `.github/workflows/ci.yml`):

```yaml
- name: Verify ML dependencies
  run: |
    pip install pip-tools
    pip-compile --generate-hashes requirements.in --dry-run
    pip install --require-hashes -r ml/requirements.locked.txt --dry-run

- name: Validate model manifest
  run: |
    python scripts/validate_model_manifest.py ml/models/MANIFEST.json
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
          pip install pip-tools
          pip-compile --generate-hashes -U requirements.in -o requirements.locked.txt
      
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
2. Run `pip-compile --generate-hashes -o requirements.locked.txt requirements.in`
3. Review the transitive dependencies added
4. Commit both files with explanation of why dependency was added

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
| Transitive lock | `pip-compile` captures all dependencies |
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
pip install --require-hashes --no-cache-dir -r requirements.locked.txt
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
