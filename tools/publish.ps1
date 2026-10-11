<#
.SYNOPSIS
Publishes and archives jaNET for one or more systems, without Python or Node.js.
.EXAMPLE
.\tools\publish.ps1 -Runtime win-x64,linux-arm64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'linux-arm', 'osx-x64', 'osx-arm64')][string[]] $Runtime,
    [switch] $FrameworkDependent,
    [switch] $Help
)
$ErrorActionPreference = 'Stop'
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET SDK 10 or later.' }
    $arguments = @()
    if ($Help) { $arguments += '--help' }
    if ($FrameworkDependent) { $arguments += '--framework-dependent' }
    $arguments += $Runtime
    & dotnet run --project (Join-Path $PSScriptRoot 'ReleasePackager/ReleasePackager.csproj') -c Release -- @arguments
    if ($LASTEXITCODE -ne 0) { throw "Release packaging failed (exit $LASTEXITCODE)." }
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
