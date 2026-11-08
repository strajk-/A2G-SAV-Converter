<#
.SYNOPSIS
  Builds the single-file release: dist\A2G-SAV-Converter.exe

.DESCRIPTION
  One .exe that contains everything: the program, the libraries (A2G.Convert, A2G.Sav), the Football Manager reader fmsave.exe
  and the patch builder / patcher (the same program opens as the patcher when a patch is attached to it).
  fmsave.exe (tools\fmsave) is built first by tools\build-fmsave.ps1 when it is missing or older than its sources (needs Python 3.12+,
  pass -Python <python.exe> if it is not found; -RebuildFmsave forces it).
  After the build the exe is copied alone into an empty folder and run with --selfcheck (no window) to check that it is complete.
#>
param([switch]$SkipSmokeTest, [switch]$RebuildFmsave, [string]$Python)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
# fmsave.exe is built from the vendored source (needs Python once): build it when it is missing or older than its inputs
$fmsave = Join-Path $repo 'tools\fmsave\fmsave.exe'
$inputs = @(Get-ChildItem (Join-Path $repo 'third_party\fmsave\fmsave-0.10.0') -Recurse -File) +
          @(Get-ChildItem (Join-Path $repo 'tools\fmtool') -Recurse -File -Include *.py) +
          @(Get-Item (Join-Path $repo 'tools\build-fmsave.ps1'))
$newest = ($inputs | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
if ($RebuildFmsave -or -not (Test-Path $fmsave) -or (Get-Item $fmsave).LastWriteTimeUtc -lt $newest) {
    $why = if ($RebuildFmsave) { 'requested' } elseif (-not (Test-Path $fmsave)) { 'missing' } else { 'older than its sources' }
    Write-Host "Building fmsave.exe ($why) ..."
    $build = Join-Path $PSScriptRoot 'build-fmsave.ps1'
    if ($Python) { & $build -Python $Python } else { & $build }
    if (-not (Test-Path $fmsave)) { throw 'fmsave.exe was not built' }
} else {
    Write-Host 'fmsave.exe is up to date.'
}

$out = Join-Path $repo 'dist'
$build = Join-Path $env:TEMP 'a2g-publish-build'
if (Test-Path $build) { Remove-Item $build -Recurse -Force }
dotnet build (Join-Path $repo 'src\A2G.SavConverter\A2G.SavConverter.csproj') -c Release --nologo -v q "-p:OutDir=$build\"
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

New-Item -ItemType Directory -Force $out | Out-Null
$exe = Join-Path $out 'A2G-SAV-Converter.exe'
Copy-Item (Join-Path $build 'A2G-SAV-Converter.exe') $exe -Force
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Built $exe ($size MB)"

if (-not $SkipSmokeTest) {
    # the exe alone in an empty folder, started in its --selfcheck mode: no window, no settings of the user are touched
    $lone = Join-Path $env:TEMP 'a2g-publish-smoke'
    if (Test-Path $lone) { Remove-Item $lone -Recurse -Force }
    New-Item -ItemType Directory $lone | Out-Null
    Copy-Item $exe $lone
    $p = Start-Process (Join-Path $lone 'A2G-SAV-Converter.exe') -ArgumentList '--selfcheck' -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "self check of the single exe failed (exit code $($p.ExitCode)): the libraries or fmsave are not embedded correctly" }
    Write-Host 'Self check passed: the exe is complete when it stands alone.'
}
