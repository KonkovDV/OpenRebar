param(
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot

try {
  python tools/examples/generate_png_example.py

  # Same arguments as tests/OpenRebar.Cli.Tests/ExamplesSnapshotTests.cs.
  $common = @("--thickness", "220", "--cover", "30", "--slab-width", "6000", "--slab-height", "4000", "--csv-culture", "invariant", "--layer", "BottomX")
  $examples = @(
    @{ Input = "examples/dxf/simple-slab/input.dxf"; Args = $common },
    @{ Input = "examples/png/simple-slab/input.png"; Args = $common + @("--px-per-mm", "0.1", "--origin-px", "24,424", "--roi-px", "24,24,600,400") }
  )

  foreach ($example in $examples) {
    $inputPath = $example.Input
    $expectedDir = Join-Path (Split-Path $inputPath -Parent) "expected"
    New-Item -ItemType Directory -Force -Path $expectedDir | Out-Null

    $work = Join-Path ([System.IO.Path]::GetTempPath()) ("openrebar-expected-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
      $workInput = Join-Path $work (Split-Path $inputPath -Leaf)
      Copy-Item $inputPath $workInput

      $cliArgs = @("run", "--project", "src/OpenRebar.Cli", "--configuration", $Configuration, "--", $workInput) + $example.Args
      dotnet @cliArgs
      if ($LASTEXITCODE -ne 2) {
        throw "Expected exit 2 for $inputPath, got $LASTEXITCODE."
      }

      Copy-Item ([System.IO.Path]::ChangeExtension($workInput, ".result.json")) (Join-Path $expectedDir "input.result.json") -Force
      Copy-Item ([System.IO.Path]::ChangeExtension($workInput, ".schedule.csv")) (Join-Path $expectedDir "input.schedule.csv") -Force
    }
    finally {
      Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }
  }
}
finally {
  Pop-Location
}
