[CmdletBinding()]
param(
    [string]$WorkerPath = ''
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($WorkerPath)) {
    $WorkerPath = Join-Path $repositoryRoot `
        'native\out\build\windows-x64\Beacon.StreamWorker\Debug\Beacon.StreamWorker.exe'
}
$WorkerPath = [System.IO.Path]::GetFullPath($WorkerPath)
if (-not (Test-Path -LiteralPath $WorkerPath -PathType Leaf)) {
    throw "Beacon StreamWorker executable was not found at '$WorkerPath'."
}

$env:BEACON_STREAM_WORKER_PATH = $WorkerPath
$testProject = Join-Path $repositoryRoot `
    'tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj'
& dotnet restore $testProject
if ($LASTEXITCODE -ne 0) { throw 'StreamWorker integration restore failed.' }
& dotnet test $testProject `
    --no-restore `
    --filter 'FullyQualifiedName~RealWorkerCompletesExplicitLifecycleWhenBinaryIsAvailable'
if ($LASTEXITCODE -ne 0) { throw 'StreamWorker process integration failed.' }
