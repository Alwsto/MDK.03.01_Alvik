[CmdletBinding()]
param(
    [ValidateRange(1024,65535)][int] $Port = 5190,
    [string] $DataDirectory = (Join-Path $env:LOCALAPPDATA 'StudentLoadAIS\Demo')
)

$ErrorActionPreference = 'Stop'
$api = Join-Path $PSScriptRoot '../Demo/API/StudentLoad.Api.dll'
$client = Join-Path $PSScriptRoot '../Demo/Desktop/StudentLoad.Desktop.exe'
$seed = Join-Path $PSScriptRoot '../Database/student-load-demo.sqlite'
if (!(Test-Path -LiteralPath $api) -or !(Test-Path -LiteralPath $client)) {
    throw 'Published demo files are missing. See README.md for the source launch.'
}
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
$url = "http://127.0.0.1:$Port"
try {
    $occupied = [Net.Sockets.TcpClient]::new('127.0.0.1', $Port)
    $occupied.Dispose()
    throw [InvalidOperationException]::new("Port $Port is already in use. Run again with -Port 5191.")
} catch [Net.Sockets.SocketException] {
    if ($_.Exception.SocketErrorCode -ne [Net.Sockets.SocketError]::ConnectionRefused) { throw }
}

$data = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DataDirectory)
[void][IO.Directory]::CreateDirectory($data)
$database = Join-Path $data 'student-load.sqlite'
if (!(Test-Path -LiteralPath $database) -and (Test-Path -LiteralPath $seed)) {
    Copy-Item -LiteralPath $seed -Destination $database
}

$oldDatabase = $env:STUDENTLOAD_DATABASE
$oldDemo = $env:STUDENTLOAD_DEMO_ACCOUNTS
$oldClientUrl = $env:STUDENTLOAD_API_URL
$oldClientData = $env:STUDENTLOAD_CLIENT_DATA
$oldPin = $env:STUDENTLOAD_CERT_SHA256
$server = $null
try {
    $env:STUDENTLOAD_DATABASE = $database
    $env:STUDENTLOAD_DEMO_ACCOUNTS = 'true'
    $env:STUDENTLOAD_API_URL = $url
    $env:STUDENTLOAD_CLIENT_DATA = Join-Path $data 'Client'
    $env:STUDENTLOAD_CERT_SHA256 = $null
    $log = Join-Path $data 'api.log'
    $errors = Join-Path $data 'api-errors.log'
    $apiDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $api).ProviderPath
    $serverArgs = '"{0}" --urls {1}' -f (Resolve-Path -LiteralPath $api).ProviderPath, $url
    $server = Start-Process -FilePath $dotnet -ArgumentList $serverArgs -WorkingDirectory $apiDirectory -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError $errors -PassThru

    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        if ($server.HasExited) { break }
        try {
            $status = Invoke-RestMethod -UseBasicParsing -Uri "$url/health" -TimeoutSec 1
            if ($status.status -eq 'ok') {
                $ready = $true
                break
            }
        } catch {
            Start-Sleep -Milliseconds 250
        }
    }
    if (!$ready) { throw "The local API did not start. See $log and $errors." }

    $clientPath = (Resolve-Path -LiteralPath $client).ProviderPath
    Start-Process -FilePath $clientPath -WorkingDirectory (Split-Path -Parent $clientPath) -Wait
} finally {
    if ($null -ne $server -and !$server.HasExited) { Stop-Process -Id $server.Id }
    $env:STUDENTLOAD_DATABASE = $oldDatabase
    $env:STUDENTLOAD_DEMO_ACCOUNTS = $oldDemo
    $env:STUDENTLOAD_API_URL = $oldClientUrl
    $env:STUDENTLOAD_CLIENT_DATA = $oldClientData
    $env:STUDENTLOAD_CERT_SHA256 = $oldPin
}

