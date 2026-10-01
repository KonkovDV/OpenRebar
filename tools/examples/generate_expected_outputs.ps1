param(
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repoRoot

try {
  python tools/examples/generate_png_example.py

  $examples = @(
    @{ Input = "examples/dxf/simple-slab/input.dxf"; Args = @("--thickness", "220", "--cover", "30", "--slab-width", "6000", "--slab-height", "4000") },
    @{ Input = "examples/png/simple-slab/input.png"; Args = @("--thickness", "220", "--cover", "30", "--slab-width", "6000", "--slab-height", "4000") }
  )

  foreach ($example in $examples) {
    $inputPath = $example.Input
    $expectedDir = Join-Path (Split-Path $inputPath -Parent) "expected"
    New-Item -ItemType Directory -Force -Path $expectedDir | Out-Null

    $cliArgs = @("run", "--project", "src/OpenRebar.Cli", "--configuration", $Configuration, "--", $inputPath) + $example.Args
    dotnet @cliArgs

    $resultPath = [System.IO.Path]::ChangeExtension($inputPath, ".result.json")
    $schedulePath = [System.IO.Path]::ChangeExtension($inputPath, ".schedule.csv")

    Copy-Item $resultPath (Join-Path $expectedDir "input.result.json") -Force
    Copy-Item $schedulePath (Join-Path $expectedDir "input.schedule.csv") -Force
  }
}
finally {
  Pop-Location
}
