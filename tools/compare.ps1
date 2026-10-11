# Builds the solution, replays the characterization script against the fresh build and diffs the result
# with tools\expected-transcript.txt: the transcript recorded from the ORIGINAL .NET Framework build
# (tools\baseline-transcript.txt) plus the deliberate, reviewed differences listed in tools\approved-differences.md.
# Exit code 0 = identical behavior.
param([string] $Configuration = 'Debug', [switch] $NoBuild, [string] $Out)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here

if (-not $NoBuild) {
    dotnet build (Join-Path $root 'jaNETFramework.sln') -c $Configuration -nologo -v:q
    if ($LASTEXITCODE -ne 0) { exit 1 }
}

$actual = if ($Out) { [IO.Path]::GetFullPath($Out) } else { Join-Path ([IO.Path]::GetTempPath()) ("janet-actual-" + [guid]::NewGuid().ToString('N') + '.txt') }
New-Item -ItemType Directory -Path (Split-Path -Parent $actual) -Force | Out-Null
Remove-Item -LiteralPath $actual -ErrorAction SilentlyContinue
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'characterize.ps1') `
    -Program (Join-Path $root "jaNETProgram\bin\$Configuration\net10.0\jaNETProgram.dll") -Out $actual
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $actual)) { Write-Host 'The replay itself failed.'; exit 1 }
$expectedPath = Join-Path $here 'expected-transcript.txt'
$expectedText = [IO.File]::ReadAllText($expectedPath).Replace("`r`n", "`n")
$actualText = [IO.File]::ReadAllText($actual).Replace("`r`n", "`n")
if ($expectedText -cne $actualText) {
    $diff = Compare-Object (Get-Content $expectedPath) (Get-Content $actual) -SyncWindow 0
    $diff | Format-Table -AutoSize | Out-String -Width 250 | Write-Host
    Write-Host "Behavior differs from expected: $($diff.Count) lines (actual transcript: $actual)"
    exit 1
}
Write-Host 'Behavior identical to expected.'
