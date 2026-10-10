<#
.SYNOPSIS
Builds jaNET on Windows. Tests and publishing are optional.
.EXAMPLE
.\build.ps1 -Test -Publish -Runtime win-x64
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [switch] $Test,
    [switch] $Publish,
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'linux-arm', 'osx-x64', 'osx-arm64')][string] $Runtime,
    [switch] $FrameworkDependent,
    [switch] $Help
)

$ErrorActionPreference = 'Stop'
if ($Help) {
    @'
Usage: .\build.ps1 [-Configuration Debug|Release] [-Test] [-Publish]
                   [-Runtime win-x64|win-arm64|linux-x64|linux-arm64|linux-arm|osx-x64|osx-arm64]
                   [-FrameworkDependent] [-Help]

Default: restore and build Release. Requires .NET SDK 10 or later.
-Test: run .NET and web tests; requires Node.js 18 or later.
-Publish: make a ready-to-run Release folder in artifacts/publish/.
          Defaults to win-x64 (win-arm64 on Windows ARM64).
-FrameworkDependent: omit bundled runtimes; requires -Publish.
Builds do not start jaNET or change its saved settings.
'@ | Write-Host
    exit 0
}

function Invoke-Tool([string] $Program, [string[]] $Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed (exit $LASTEXITCODE)." }
}

try {
    if (($Runtime -or $FrameworkDependent) -and -not $Publish) { throw '-Runtime and -FrameworkDependent require -Publish.' }
    if ($Publish -and $Configuration -ne 'Release') { throw 'Publishing uses Release. Omit -Configuration Debug when publishing.' }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET SDK 10 or later: https://dotnet.microsoft.com/download' }
    $sdk = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^(\d+)\.' -or [int]$Matches[1] -lt 10) { throw 'jaNET requires .NET SDK 10 or later, not only the runtime.' }

    if ($Test) {
        if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw '-Test requires Node.js 18 or later for the web tests.' }
        $nodeVersion = & node --version
        if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v(\d+)\.' -or [int]$Matches[1] -lt 18) { throw '-Test requires Node.js 18 or later.' }
    }
    if ($Publish) {
        if (-not $Runtime) {
            $architecture = $env:PROCESSOR_ARCHITEW6432
            if (-not $architecture) { $architecture = $env:PROCESSOR_ARCHITECTURE }
            switch ($architecture) {
                'AMD64' { $Runtime = 'win-x64' }
                'ARM64' { $Runtime = 'win-arm64' }
                default { throw 'Cannot detect a supported Windows architecture. Specify -Runtime explicitly.' }
            }
        }
    }

    Push-Location -LiteralPath $PSScriptRoot
    try {
        Invoke-Tool dotnet @('restore', 'jaNETFramework.sln', '--nologo')
        Invoke-Tool dotnet @('build', 'jaNETFramework.sln', '-c', $Configuration, '--no-restore', '--nologo')
        if ($Test) {
            Invoke-Tool dotnet @('test', 'jaNETFramework.sln', '-c', $Configuration, '--no-build', '--no-restore', '--nologo')
            $webTests = @(Get-ChildItem -LiteralPath 'jaNETFramework.Tests' -Filter 'Web*Tests.mjs' | Sort-Object Name | ForEach-Object { $_.FullName })
            Invoke-Tool node (@('--test') + $webTests)
        }
        if ($Publish) {
            $selfContained = 'true'; $suffix = ''
            if ($FrameworkDependent) { $selfContained = 'false'; $suffix = '-framework-dependent' }
            $output = Join-Path $PSScriptRoot "artifacts\publish\$Runtime$suffix"
            Invoke-Tool dotnet @('publish', 'jaNETProgram', '-c', 'Release', '-r', $Runtime, '--self-contained', $selfContained, '-o', $output, '--nologo')
            foreach ($file in @('LICENSE', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.md', 'README.md')) {
                Copy-Item -LiteralPath $file -Destination $output -Force
            }
            if ($Runtime.StartsWith('linux-')) { Copy-Item -LiteralPath 'deploy\janet.service' -Destination $output -Force }
            Write-Host "Published: $output"
        }
        Write-Host "Build ready: $(Join-Path $PSScriptRoot "jaNETProgram\bin\$Configuration\net10.0")"
        Write-Host "Run: dotnet run --project jaNETProgram -c $Configuration --no-build"
    } finally { Pop-Location }
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
