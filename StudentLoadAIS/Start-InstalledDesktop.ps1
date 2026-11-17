[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Installation
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $Installation).ProviderPath
$version = (Get-Content -LiteralPath (Join-Path $root 'active-version.txt') -Raw).Trim()
if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw 'The active version file is invalid.'
}
$versionFolder = Join-Path (Join-Path $root 'versions') $version
$executable = Join-Path $versionFolder 'StudentLoad.Desktop.exe'
if (!(Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'The executable for the active version is missing.'
}
& $executable

