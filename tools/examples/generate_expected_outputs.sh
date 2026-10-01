#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
cd "${REPO_ROOT}"

python tools/examples/generate_png_example.py

run_example() {
  local input_path="$1"
  shift

  local expected_dir
  expected_dir="$(dirname "${input_path}")/expected"
  mkdir -p "${expected_dir}"

  dotnet run --project src/OpenRebar.Cli --configuration Release -- "${input_path}" "$@"

  local result_path="${input_path%.*}.result.json"
  local schedule_path="${input_path%.*}.schedule.csv"

  cp "${result_path}" "${expected_dir}/input.result.json"
  cp "${schedule_path}" "${expected_dir}/input.schedule.csv"
}

run_example "examples/dxf/simple-slab/input.dxf" --thickness 220 --cover 30 --slab-width 6000 --slab-height 4000
run_example "examples/png/simple-slab/input.png" --thickness 220 --cover 30 --slab-width 6000 --slab-height 4000
