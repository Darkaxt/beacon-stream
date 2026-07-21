param(
    [int]$Port = 49680,
    [string]$StateDirectory = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($StateDirectory)) {
    $StateDirectory = Join-Path $repositoryRoot ".artifacts\emulator-stage"
}
$StateDirectory = [System.IO.Path]::GetFullPath($StateDirectory)
$server = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot "src\Beacon.Server\bin\Debug\net10.0-windows\Beacon.Server.dll"))
$worker = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot "native\out\build\windows-x64\Beacon.StreamWorker\Debug\Beacon.StreamWorker.exe"))

foreach ($requiredPath in @(
    $server,
    $worker,
    (Join-Path $StateDirectory "credentials.json"),
    (Join-Path $StateDirectory "profiles.json"),
    (Join-Path $StateDirectory "manual-games.json")
)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required emulator-stage artifact is unavailable: $requiredPath"
    }
}

$pidPath = Join-Path $StateDirectory "server.pid"
if (Test-Path -LiteralPath $pidPath) {
    $existingProcessId = [int](Get-Content -Raw -LiteralPath $pidPath)
    if ($null -ne (Get-Process -Id $existingProcessId -ErrorAction SilentlyContinue)) {
        throw "Beacon emulator-stage Server is already running as process $existingProcessId."
    }
}

$env:Beacon__Streaming__WorkerPath = $worker
$env:Beacon__Security__IdentityPath = Join-Path $StateDirectory "server-identity.pfx"
$env:Beacon__Security__CredentialsPath = Join-Path $StateDirectory "credentials.json"
$env:Beacon__Profiles__Path = Join-Path $StateDirectory "profiles.json"
$env:Beacon__Benchmarks__Path = Join-Path $StateDirectory "benchmarks.json"
$env:Beacon__Displays__NameMapPath = Join-Path $StateDirectory "display-name-map.json"
$env:Beacon__Games__ManualPath = Join-Path $StateDirectory "manual-games.json"
$env:Beacon__Games__SteamRoot = Join-Path $StateDirectory "steam"
$env:Beacon__Games__HeroicRoot = Join-Path $StateDirectory "heroic"
$env:Beacon__Games__HydraDatabasePath = Join-Path $StateDirectory "hydra.db"
$env:BEACON_SESSION_PROBE_EVIDENCE = Join-Path $StateDirectory "session-probe.jsonl"
$env:BEACON_SESSION_PROBE_RUN_ID = "emulator-stage"
$env:Logging__Console__FormatterName = "json"
$env:Logging__LogLevel__Default = "Information"
$env:Logging__LogLevel__Microsoft_AspNetCore = "Warning"

$process = Start-Process `
    -FilePath "dotnet" `
    -ArgumentList @($server, "--urls", "https://0.0.0.0:$Port") `
    -WorkingDirectory (Split-Path -Parent $server) `
    -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $StateDirectory "server.stdout.log") `
    -RedirectStandardError (Join-Path $StateDirectory "server.stderr.log") `
    -PassThru
$process.Id | Set-Content -LiteralPath $pidPath -NoNewline

Write-Output "Beacon emulator-stage Server started pid=$($process.Id) port=$Port."
