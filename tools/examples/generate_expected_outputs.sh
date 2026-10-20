#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
cd "${REPO_ROOT}"

python tools/examples/generate_png_example.py

# Same arguments as tests/OpenRebar.Cli.Tests/ExamplesSnapshotTests.cs.
COMMON=(--thickness 220 --cover 30 --slab-width 6000 --slab-height 4000 --csv-culture invariant --layer BottomX)

run_example() {
  local input_path="$1"
  shift

  local expected_dir
  expected_dir="$(dirname "${input_path}")/expected"
  mkdir -p "${expected_dir}"

  local work
  work="$(mktemp -d)"
  local work_input="${work}/$(basename "${input_path}")"
  cp "${input_path}" "${work_input}"

  local status=0
  dotnet run --project src/OpenRebar.Cli --configuration Release -- "${work_input}" "$@" || status=$?
  if [ "${status}" -ne 2 ]; then
    rm -rf "${work}"
    echo "Expected exit 2 for ${input_path}, got ${status}." >&2
    exit 1
  fi

  cp "${work_input%.*}.result.json" "${expected_dir}/input.result.json"
  cp "${work_input%.*}.schedule.csv" "${expected_dir}/input.schedule.csv"
  rm -rf "${work}"
}

run_example "examples/dxf/simple-slab/input.dxf" "${COMMON[@]}"
run_example "examples/png/simple-slab/input.png" "${COMMON[@]}" --px-per-mm 0.1 --origin-px 24,424 --roi-px 24,24,600,400
