param(
    [string]$StateDirectory = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($StateDirectory)) {
    $StateDirectory = Join-Path $repositoryRoot ".artifacts\emulator-stage"
}
$pidPath = Join-Path ([System.IO.Path]::GetFullPath($StateDirectory)) "server.pid"
if (-not (Test-Path -LiteralPath $pidPath -PathType Leaf)) {
    Write-Output "Beacon emulator-stage Server is not recorded as running."
    exit 0
}

$serverProcessId = [int](Get-Content -Raw -LiteralPath $pidPath)
$process = Get-Process -Id $serverProcessId -ErrorAction SilentlyContinue
if ($null -ne $process) {
    Stop-Process -Id $serverProcessId
    Wait-Process -Id $serverProcessId -ErrorAction SilentlyContinue
}
Remove-Item -LiteralPath $pidPath -Force
Write-Output "Beacon emulator-stage Server stopped."
