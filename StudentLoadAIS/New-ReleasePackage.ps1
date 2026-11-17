[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $DesktopFolder,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')][string] $Version,
    [Parameter(Mandatory)][string] $Destination
)

$ErrorActionPreference = 'Stop'
$client = (Resolve-Path -LiteralPath $DesktopFolder).ProviderPath
if (!(Test-Path -LiteralPath (Join-Path $client 'StudentLoad.Desktop.exe') -PathType Leaf)) {
    throw 'Run dotnet publish before packaging the desktop release.'
}
$release = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
[void][IO.Directory]::CreateDirectory($release)
$name = "student-load-$Version.zip"
$package = Join-Path $release $name
$manifestPath = Join-Path $release 'manifest.json'
if (Test-Path -LiteralPath $package) {
    throw 'A package for this version already exists. Publish a new version.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($client, $package)
$manifest = [ordered]@{
    version = $Version
    package = $name
    sha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
}
$temporary = $manifestPath + '.' + [Guid]::NewGuid().ToString('N')
try {
    [IO.File]::WriteAllText($temporary, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $manifestPath -Force
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
Write-Output $manifestPath

