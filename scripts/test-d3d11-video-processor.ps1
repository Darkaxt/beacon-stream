[CmdletBinding()]
param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '..\native\out\build\windows-x64'),
    [string]$DisplayName,
    [uint32]$DisplayWidth,
    [uint32]$DisplayHeight,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$revision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($revision)) {
    throw 'Could not resolve the Beacon source revision.'
}
Write-Output "BEACON_SOURCE_REVISION $revision"

if (-not $SkipBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'Visual Studio Installer vswhere.exe is required to build the probes.'
    }
    $installationPath = (& $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($installationPath)) {
        throw 'Visual Studio with the x64 C++ toolchain is required to build the probes.'
    }
    $developerShell = Join-Path $installationPath 'Common7\Tools\Launch-VsDevShell.ps1'
    & $developerShell -Arch amd64 -HostArch amd64 -SkipAutomaticLocation

    Push-Location (Join-Path $repositoryRoot 'native')
    try {
        & cmake --build --preset windows-x64-debug --target `
            BeaconStreamWorkerD3d11VideoProcessorProbe `
            BeaconStreamWorkerWgcCaptureProbe
        if ($LASTEXITCODE -ne 0) {
            throw 'D3D11 video probe build failed.'
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-CheckedProbe(
    [string]$Path,
    [string[]]$Arguments,
    [scriptblock]$Validate
) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required native probe is missing: $Path"
    }

    $output = @(& $Path @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $output | Write-Output
    if ($exitCode -ne 0) {
        throw "Native probe failed with exit code $exitCode`: $Path"
    }
    if (-not (& $Validate $output)) {
        throw "Native probe output did not contain the required evidence: $Path"
    }
}

$configurationDirectory = Join-Path $BuildDirectory 'Beacon.StreamWorker.Tests\Debug'
$processorProbe = Join-Path $configurationDirectory 'BeaconStreamWorkerD3d11VideoProcessorProbe.exe'
Invoke-CheckedProbe $processorProbe @() {
    param([string[]]$Output)

    ($Output -contains 'letterbox_yuv=16,128,128') -and
    ($Output -match '^BEACON_D3D11_VIDEO_PROCESSOR_OK .* input=1280x720 output=640x400 tolerance=5$')
}

$displayRequested = -not [string]::IsNullOrWhiteSpace($DisplayName)
if ($displayRequested -and ($DisplayWidth -eq 0 -or $DisplayHeight -eq 0)) {
    throw 'DisplayWidth and DisplayHeight are required when DisplayName is provided.'
}
if (-not $displayRequested -and ($DisplayWidth -ne 0 -or $DisplayHeight -ne 0)) {
    throw 'DisplayName is required when display dimensions are provided.'
}

if ($displayRequested) {
    $wgcProbe = Join-Path $configurationDirectory 'BeaconStreamWorkerWgcCaptureProbe.exe'
    Invoke-CheckedProbe $wgcProbe @($DisplayName, "$DisplayWidth", "$DisplayHeight") {
        param([string[]]$Output)

        $Output -match '^BEACON_WGC_CAPTURE_OK .* format=NV12 .* qpc1=\d+ qpc2=\d+$'
    }
}

Write-Output 'BEACON_D3D11_VIDEO_VALIDATION_OK'
