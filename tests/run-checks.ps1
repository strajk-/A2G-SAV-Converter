<#
.SYNOPSIS
  Builds the solution and runs every automated check. Nothing in the real Anstoss 2 install is modified.

.DESCRIPTION
  1. A2G.Sav.RoundTrip   - every original .sav file parses and re-serialises byte-identically
  2. A2G.Convert.Cli     - builds a plan from an fm_export folder, writes the .sav files into a scratch folder and validates them
  3. editor-selftest     - swaps, pool replacements, squad moves, saved edits replayed on a rebuilt plan
  4. profiles-selftest   - profile manager on a scratch COPY of the data folders

  powershell -File tests\run-checks.ps1 -Anstoss2 "<your Anstoss 2 folder>" -FmExport "<folder with the FM export (fm_export of a project)>"
#>
param(
    [Parameter(Mandatory = $true)][string]$Anstoss2,
    [Parameter(Mandatory = $true)][string]$FmExport
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $env:TEMP 'a2g-checks'
if (Test-Path $scratch) { Remove-Item $scratch -Recurse -Force }
New-Item -ItemType Directory -Force $scratch | Out-Null

dotnet build (Join-Path $repo 'A2G-SAV-Converter.sln') -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$roundTrip = Join-Path $repo 'tests\A2G.Sav.RoundTrip\bin\Debug\net48\A2G.Sav.RoundTrip.exe'
$cli = Join-Path $repo 'tests\A2G.Convert.Cli\bin\Debug\net48\A2G.Convert.Cli.exe'
$failed = @()

function Run($name, [scriptblock]$block) {
    Write-Host "`n=== $name"
    & $block
    if ($LASTEXITCODE -ne 0) { $script:failed += $name; Write-Host "FAILED: $name" -ForegroundColor Red }
}

Run 'round trip of the original files' { & $roundTrip $Anstoss2 | Select-Object -Last 3 }
Run 'convert + write + validate'       { & $cli $FmExport $Anstoss2 (Join-Path $scratch 'Anstoss2') | Select-String 'validation|WARNING' }
Run 'round trip of the written files'  { & $roundTrip (Join-Path $scratch 'Anstoss2') | Select-Object -Last 3 }
Run 'plan editor'                      { & $cli editor-selftest $FmExport $Anstoss2 (Join-Path $scratch 'editor') | Select-Object -Last 4 }
Run 'profile manager (scratch copy)'   { & $cli profiles-selftest (Join-Path $scratch 'profiles') $Anstoss2 (Join-Path $scratch 'Anstoss2') | Select-Object -Last 3 }
Run 'patch file and installation'      { & $cli patch-selftest (Join-Path $scratch 'patch') $Anstoss2 (Join-Path $scratch 'Anstoss2') | Select-Object -Last 3 }
Run 'project list'                    { & $cli project-selftest (Join-Path $scratch 'projects') | Select-Object -Last 3 }

Write-Host ''
if ($failed.Count -eq 0) { Write-Host 'All checks passed.' -ForegroundColor Green; exit 0 }
Write-Host ("Failed: " + ($failed -join ', ')) -ForegroundColor Red
exit 1
