# Builds the solution, replays the characterization script against the fresh build and diffs the result
# with tools\expected-transcript.txt: the transcript recorded from the ORIGINAL .NET Framework build
# (tools\baseline-transcript.txt) plus the deliberate, reviewed differences listed in tools\approved-differences.md.
# Exit code 0 = identical behavior.
param([string] $Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here

dotnet build (Join-Path $root 'jaNETFramework.sln') -c $Configuration -nologo -v:q
if ($LASTEXITCODE -ne 0) { exit 1 }

$actual = Join-Path ([IO.Path]::GetTempPath()) 'janet-actual-transcript.txt'
Remove-Item -LiteralPath $actual -ErrorAction SilentlyContinue   # never compare a leftover of an earlier run
& powershell -NoProfile -File (Join-Path $here 'characterize.ps1') `
    -Program (Join-Path $root "jaNETProgram\bin\$Configuration\net10.0\jaNETProgram.dll") -Out $actual
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $actual)) { Write-Host 'The replay itself failed.'; exit 1 }
$diff = Compare-Object (Get-Content (Join-Path $here 'expected-transcript.txt')) (Get-Content $actual)
if ($diff) {
    $diff | Format-Table -AutoSize | Out-String -Width 250 | Write-Host
    Write-Host "Behavior differs from expected: $($diff.Count) lines (actual transcript: $actual)"
    exit 1
}
Write-Host 'Behavior identical to expected.'
