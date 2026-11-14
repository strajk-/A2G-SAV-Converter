<#
.SYNOPSIS
  Builds tools\fmsave\fmsave.exe from the vendored fmsave source in third_party\fmsave.

.DESCRIPTION
  Creates a private virtual environment in tools\_build (gitignored), installs the vendored
  fmsave source plus PyInstaller into it, and freezes fmsave's CLI into a single exe.
  The WPF app copies tools\fmsave\* next to itself at build time and runs fmsave.exe from there.
  Requires Python 3.12+ on the machine (only needed to run this script, not to run the app).
#>
[CmdletBinding()]
param(
    [string]$Python
)
$ErrorActionPreference = 'Stop'

$repo      = Split-Path -Parent $PSScriptRoot
$source    = Join-Path $repo 'third_party\fmsave\fmsave-0.12.4'
$buildDir  = Join-Path $PSScriptRoot '_build'
$venv      = Join-Path $buildDir 'venv'
$outDir    = Join-Path $PSScriptRoot 'fmsave'
$entry     = Join-Path $PSScriptRoot 'fmtool\fmtool_entry.py'

if (-not $Python) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python314\python.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python313\python.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe')
    )
    $Python = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Python -or -not (Test-Path $Python)) { throw 'Python 3.12+ not found. Pass -Python <path to python.exe>.' }

New-Item -ItemType Directory -Force $buildDir | Out-Null
if (-not (Test-Path (Join-Path $venv 'Scripts\python.exe'))) {
    & $Python -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw 'venv creation failed' }
}
$vpy = Join-Path $venv 'Scripts\python.exe'

& $vpy -m pip install --disable-pip-version-check --quiet --upgrade pip
& $vpy -m pip install --disable-pip-version-check --quiet $source pyinstaller
if ($LASTEXITCODE -ne 0) { throw 'pip install failed' }

# $entry (tools\fmtool\fmtool_entry.py) wraps fmsave's CLI and adds our own commands (e.g. a2g-nations).
if (-not (Test-Path $entry)) { throw "Missing entry script: $entry" }

$work = Join-Path $buildDir 'pyinstaller'
& $vpy -m PyInstaller --noconfirm --clean --onefile --console --name fmsave `
    --distpath $outDir --workpath (Join-Path $work 'work') --specpath $work `
    --collect-submodules fmsave $entry
if ($LASTEXITCODE -ne 0) { throw 'PyInstaller failed' }

Write-Host "Built $(Join-Path $outDir 'fmsave.exe')"
& (Join-Path $outDir 'fmsave.exe') --version
