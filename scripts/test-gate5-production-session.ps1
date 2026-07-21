[CmdletBinding()]
param(
    [string]$Serial = 'emulator-5554',
    [string]$EvidenceDirectory,
    [string]$BeforeConnectSignalPath,
    [switch]$ArtifactsReady
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $ArtifactsReady) {
    & dotnet build (Join-Path $repositoryRoot 'src\Beacon.Server\Beacon.Server.csproj') `
        --configuration Debug --warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Beacon Server build failed.' }
    & dotnet build (Join-Path $repositoryRoot 'tests\Beacon.SessionProbe\Beacon.SessionProbe.csproj') `
        --configuration Debug --warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Beacon SessionProbe build failed.' }
    & dotnet build (Join-Path $repositoryRoot 'tests\Beacon.ProductionAcceptance\Beacon.ProductionAcceptance.csproj') `
        --configuration Debug --warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Beacon production acceptance runner build failed.' }
    & (Join-Path $repositoryRoot 'scripts\build-native-windows.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Beacon StreamWorker build failed.' }
    & (Join-Path $repositoryRoot 'scripts\test-android.ps1') `
        -Tasks @('assembleDebug', 'assembleDebugAndroidTest')
    if ($LASTEXITCODE -ne 0) { throw 'Beacon Android APK build failed.' }
}

$arguments = @(
    'run',
    '--project',
    (Join-Path $repositoryRoot 'tests\Beacon.ProductionAcceptance\Beacon.ProductionAcceptance.csproj'),
    '--no-build',
    '--',
    '--repository-root',
    $repositoryRoot,
    '--serial',
    $Serial
)
if (-not [string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $arguments += @('--evidence-directory', $EvidenceDirectory)
}
if (-not [string]::IsNullOrWhiteSpace($BeforeConnectSignalPath)) {
    $arguments += @('--before-connect-signal', $BeforeConnectSignalPath)
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw 'Beacon Gate 5 production acceptance failed.'
}
