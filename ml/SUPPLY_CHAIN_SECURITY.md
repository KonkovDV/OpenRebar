# ML Supply-Chain Security

The ML lane is optional. A run without `--ml-url` never calls it.

## The lock

`ml/requirements.locked.txt` is the CPython 3.11 manylinux resolution of `ml/requirements.in`. Every package carries its sha256. torch is `2.13.0+cpu` from the PyTorch CPU index; numpy stays on 1.26.

CI installs that file and does not rewrite it. The `python-smoke` job:

1. installs the lock with `--require-hashes`;
2. checks that the lock still carries hash entries;
3. validates `ml/models/MANIFEST.json`;
4. runs `pytest tests -q`.

Refresh the lock from `ml/` with the pinned pip-tools bootstrap and the same platform tags as the lock header:

```bash
python -m pip install --require-hashes -r ../.github/requirements/pip-tools.locked.txt
python -m piptools compile --allow-unsafe --generate-hashes --newline lf --output-file=requirements.locked.txt --pip-args "--python-version 3.11 --platform manylinux_2_28_x86_64 --platform manylinux_2_17_x86_64 --platform manylinux2014_x86_64 --implementation cp --abi cp311 --only-binary=:all:" requirements.in
```

numpy 1.26 wheels are tagged `manylinux_2_17`, which is why that tag is on the list. A lock compiled for Linux does not install on Windows; the `python-smoke` job on GitHub is the install check.

## Models

`ml/models/MANIFEST.json` lists the model files a service may load: a unique `model_id`, a relative `filename`, and a lowercase `sha256`. The list is empty. No checkpoint lives in git.

At startup the service looks for `OpenRebar_MODEL_PATH`, by default `ml/models/isoline_unet.pt`. If the file is there, it is hashed in 1 MB blocks and compared with its manifest entry before torch reads it. `OpenRebar_MODEL_SHA256` replaces the manifest entry for a model mounted from elsewhere. A missing entry, an absolute path, a `..` segment, or a different digest stops startup. torch then loads the weights with `weights_only=True`.

```bash
python ml/scripts/validate_model_manifest.py ml/models/MANIFEST.json
python ml/scripts/validate_model_manifest.py --verify-files ml/models/MANIFEST.json
```

The second command also hashes every listed file, for a downloaded bundle.

## The service

The FastAPI service binds `127.0.0.1` and has no authentication. Put it behind a proxy that adds one before anything else can reach it. An upload is at most 20 MB, a frame at most 40 million pixels (`OpenRebar_MAX_IMAGE_PIXELS`), one inference runs at a time, and each has 120 s. The file type comes from its first bytes, not from the client's file name.

The C# client talks to loopback, plus the host named by `--ml-url`. It refuses a redirect to any other host.

## OSV

`ml/osv-scanner.toml` is the OSV-Scanner ignore file for this directory. It holds 164 advisory ids under one shared reason, all expiring on 2026-12-31. Replacing them with one entry per package and version is open; see [docs/AUDIT_2026-10-20.md](../docs/AUDIT_2026-10-20.md).
