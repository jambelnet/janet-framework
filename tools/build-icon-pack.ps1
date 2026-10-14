# (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
# Regenerates the committed SVG artwork and the downloadable icon archive.
[CmdletBinding()]
param([string] $NodePath = 'node')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
& $NodePath (Join-Path $PSScriptRoot 'build-icons.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Default icon generation failed.' }
$icons = Join-Path $root 'www\icons'
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $icons 'LICENSE-jaNET.txt')
$paths = @('default', 'catalog.json', 'README.txt', 'LICENSE-lucide.txt', 'LICENSE-jaNET.txt') | ForEach-Object { Join-Path $icons $_ }
Compress-Archive -LiteralPath $paths -DestinationPath (Join-Path $icons 'jubito-default-icons.zip') -CompressionLevel Optimal -Force
Get-Item -LiteralPath (Join-Path $icons 'jubito-default-icons.zip') | Select-Object FullName, Length
