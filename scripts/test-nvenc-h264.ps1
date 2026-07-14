[CmdletBinding()]
param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '..\native\out\build\windows-x64'),
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$native = Join-Path $root 'native'
$revision = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($revision)) {
    throw 'Could not resolve the Beacon source revision.'
}
Write-Output "BEACON_SOURCE_REVISION $revision"

if (-not $SkipBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'Visual Studio Installer vswhere.exe is required to build the NVENC probe.'
    }
    $installationPath = (& $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($installationPath)) {
        throw 'Visual Studio with the x64 C++ toolchain is required to build the NVENC probe.'
    }
    $developerShell = Join-Path $installationPath 'Common7\Tools\Launch-VsDevShell.ps1'
    & $developerShell -Arch amd64 -HostArch amd64 -SkipAutomaticLocation

    Push-Location $native
    try {
        cmake --build --preset windows-x64-debug --target `
            BeaconStreamWorkerNvencH264EncoderProbe
        if ($LASTEXITCODE -ne 0) {
            throw "NVENC probe build failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

$probe = Join-Path $BuildDirectory 'Beacon.StreamWorker.Tests\Debug\BeaconStreamWorkerNvencH264EncoderProbe.exe'
$output = Join-Path $env:TEMP ("beacon-nvenc-{0}.h264" -f [guid]::NewGuid().ToString('N'))

try {
    if (-not (Test-Path -LiteralPath $probe -PathType Leaf)) {
        throw "Required NVENC probe is missing: $probe"
    }
    foreach ($tool in 'ffprobe', 'ffmpeg') {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            throw "$tool is required as a test-only H.264 decoder oracle."
        }
    }

    $probeOutput = @(& $probe $output 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "NVENC probe failed with exit code $LASTEXITCODE.`n$($probeOutput -join [Environment]::NewLine)"
    }
    $probeOutput | Write-Output
    if (($probeOutput -join "`n") -notmatch 'BEACON_NVENC_H264_OK') {
        throw 'NVENC probe did not emit its success marker.'
    }

    $probeInfo = & ffprobe -v error -f h264 -select_streams v:0 `
        -show_entries stream=codec_name,profile,width,height,pix_fmt,has_b_frames,color_range,color_space,color_transfer,color_primaries `
        -of default=noprint_wrappers=1 $output 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "ffprobe rejected the Annex-B stream.`n$($probeInfo -join [Environment]::NewLine)"
    }
    $probeInfo | Write-Output
    $decoded = & ffmpeg -v error -f h264 -i $output -frames:v 4 -f null NUL 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed to decode four frames.`n$($decoded -join [Environment]::NewLine)"
    }
    if (($probeInfo -join "`n") -notmatch 'codec_name=h264' -or
        ($probeInfo -join "`n") -notmatch 'width=640' -or
        ($probeInfo -join "`n") -notmatch 'height=400' -or
        ($probeInfo -join "`n") -notmatch 'pix_fmt=yuv420p' -or
        ($probeInfo -join "`n") -notmatch 'profile=High' -or
        ($probeInfo -join "`n") -notmatch 'has_b_frames=0' -or
        ($probeInfo -join "`n") -notmatch 'color_range=tv' -or
        ($probeInfo -join "`n") -notmatch 'color_space=bt709' -or
        ($probeInfo -join "`n") -notmatch 'color_transfer=bt709' -or
        ($probeInfo -join "`n") -notmatch 'color_primaries=bt709') {
        throw 'Decoded H.264 stream metadata did not match the Beacon encoder plan.'
    }

    $timing = @(& ffmpeg -loglevel trace -f h264 -i $output -c copy `
        -bsf:v trace_headers -f null NUL 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw 'ffmpeg could not inspect the H.264 SPS timing metadata.'
    }
    $tickMatch = $timing | Select-String -Pattern 'num_units_in_tick.* = (?<value>\d+)$' | Select-Object -First 1
    $scaleMatch = $timing | Select-String -Pattern 'time_scale.* = (?<value>\d+)$' | Select-Object -First 1
    if (-not $tickMatch -or -not $scaleMatch) {
        throw 'The H.264 SPS did not expose timing metadata.'
    }
    $ticks = [uint64]$tickMatch.Matches[0].Groups['value'].Value
    $timeScale = [uint64]$scaleMatch.Matches[0].Groups['value'].Value
    if ($ticks -eq 0 -or $timeScale -ne (2 * 120 * $ticks)) {
        throw "The H.264 SPS timing does not represent 120 fps: time_scale=$timeScale num_units_in_tick=$ticks."
    }
    Write-Output "sps_frame_rate=120/1 time_scale=$timeScale num_units_in_tick=$ticks"
    Write-Output 'BEACON_NVENC_H264_VALIDATION_OK frames=4 decoded=4'
}
finally {
    Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
}
