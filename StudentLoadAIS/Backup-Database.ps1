[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Database,
    [Parameter(Mandatory)][string] $Destination
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../StudentLoad.Operations/StudentLoad.Operations.csproj'
$sourcePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Database)
$destinationPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)

& dotnet run --project $project -- $sourcePath $destinationPath
if ($LASTEXITCODE -ne 0) {
    throw "SQLite backup failed (exit code $LASTEXITCODE)."
}

