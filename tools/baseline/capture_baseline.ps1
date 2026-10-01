# Captures pipeline KPIs for the synthetic corpus (plan A0).
# DXF and PNG are generated into a temp directory and are not committed.
# Usage: pwsh tools/baseline/capture_baseline.ps1

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$corpusProject = Join-Path $root "tests\OpenRebar.TestCorpus\OpenRebar.TestCorpus.csproj"
$cliProject = Join-Path $root "src\OpenRebar.Cli\OpenRebar.Cli.csproj"
$outDir = Join-Path ([System.IO.Path]::GetTempPath()) ("openrebar-corpus-" + [DateTime]::UtcNow.ToString("yyyyMMddHHmmss"))
$baselinePath = Join-Path $root "docs\baseline\2026-10-01.json"

Write-Host "Generating corpus into $outDir"
dotnet run --project $corpusProject -c Release -- generate $outDir
if ($LASTEXITCODE -ne 0) { throw "Corpus generation failed." }

Write-Host "Building CLI"
dotnet build $cliProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "CLI build failed." }
$cli = Join-Path $root "src\OpenRebar.Cli\bin\Release\net8.0\OpenRebar.Cli.exe"
if (-not (Test-Path $cli)) { throw "CLI executable not found: $cli" }

function Invoke-Pipeline {
  param(
    [string] $Cli,
    [string] $InputPath,
    [string] $Width,
    [string] $Height,
    [string] $Thickness,
    [string] $Cover
  )

  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $Cli
  $psi.Arguments = "`"$InputPath`" --slab-width $Width --slab-height $Height --thickness $Thickness --cover $Cover"
  $psi.UseShellExecute = $false
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.CreateNoWindow = $true

  $process = New-Object System.Diagnostics.Process
  $process.StartInfo = $psi
  [void]$process.Start()
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $timedOut = -not $process.WaitForExit(300000)
  if ($timedOut) {
    try { $process.Kill() } catch { }
    return [ordered]@{
      exitCode = 124
      timedOut = $true
      parsedZoneCount = $null
      totalRebarSegments = $null
      totalMassKg = $null
      totalWastePercent = $null
      stockBarsNeeded = $null
      optimizerId = $null
      errors = @("timeout")
    }
  }

  $stdout = $stdoutTask.Result
  $stderr = $stderrTask.Result
  $resultPath = [System.IO.Path]::ChangeExtension($InputPath, ".result.json")
  $parsedZoneCount = $null
  $segments = $null
  $mass = $null
  $waste = $null
  $stock = $null
  $optimizerId = $null
  $errors = @()

  if (Test-Path $resultPath) {
    $report = Get-Content $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($report.summary) {
      $parsedZoneCount = $report.summary.parsedZoneCount
      $segments = $report.summary.totalRebarSegments
      $mass = $report.summary.totalMassKg
      $waste = $report.summary.totalWastePercent
    }
    if ($report.analysisProvenance -and $report.analysisProvenance.optimization) {
      $optimizerId = $report.analysisProvenance.optimization.optimizerId
    }
    $stock = 0
    foreach ($byDiameter in @($report.optimizationByDiameter)) {
      if ($null -ne $byDiameter -and $null -ne $byDiameter.stockBarsNeeded) { $stock += [int]$byDiameter.stockBarsNeeded }
    }
    foreach ($failure in @($report.errors)) {
      if ($null -ne $failure -and $failure.errorMessage) { $errors += [string]$failure.errorMessage }
    }
  }
  elseif ($stderr) {
    $errors = @($stderr.Trim())
  }

  return [ordered]@{
    exitCode = $process.ExitCode
    timedOut = $false
    parsedZoneCount = $parsedZoneCount
    totalRebarSegments = $segments
    totalMassKg = $mass
    totalWastePercent = $waste
    stockBarsNeeded = $stock
    optimizerId = $optimizerId
    errors = $errors
    stderrTail = if ($stderr.Length -gt 500) { $stderr.Substring($stderr.Length - 500) } else { $stderr }
  }
}

$invariant = [System.Globalization.CultureInfo]::InvariantCulture
$scenarios = [System.Collections.Generic.List[object]]::new()

Get-ChildItem $outDir -Directory | Sort-Object Name | ForEach-Object {
  $id = $_.Name
  $truthPath = Join-Path $_.FullName "ground_truth.json"
  $truth = Get-Content $truthPath -Raw -Encoding UTF8 | ConvertFrom-Json
  $width = $truth.cli.slabWidthMm.ToString($invariant)
  $height = $truth.cli.slabHeightMm.ToString($invariant)
  $thickness = $truth.cli.thicknessMm.ToString($invariant)
  $cover = $truth.cli.coverMm.ToString($invariant)

  $entry = [ordered]@{
    id = $id
    dxf = Invoke-Pipeline -Cli $cli -InputPath (Join-Path $_.FullName "input.dxf") -Width $width -Height $height -Thickness $thickness -Cover $cover
    png = Invoke-Pipeline -Cli $cli -InputPath (Join-Path $_.FullName "input.png") -Width $width -Height $height -Thickness $thickness -Cover $cover
  }
  $scenarios.Add($entry)
  Write-Host ("{0}: dxf exit {1}, png exit {2}" -f $id, $entry.dxf.exitCode, $entry.png.exitCode)
}

$commit = (git -C $root rev-parse HEAD).Trim()
$dirty = [bool](git -C $root status --porcelain)

$document = [ordered]@{
  capturedAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
  gitCommit = $commit
  workingTreeDirty = $dirty
  note = "KPI of the pipeline before Phase A fixes. These numbers describe the current behaviour; they are not targets."
  generator = "OpenRebar.TestCorpus/v0"
  scenarios = $scenarios
}

New-Item -ItemType Directory -Force -Path (Split-Path $baselinePath) | Out-Null
$json = $document | ConvertTo-Json -Depth 8
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($baselinePath, $json + "`n", $utf8)
Write-Host "Wrote $baselinePath"
