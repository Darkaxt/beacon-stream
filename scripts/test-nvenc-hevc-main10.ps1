[CmdletBinding()]
param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '..\native\out\build\windows-x64'),
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$native = Join-Path $root 'native'

if (-not $SkipBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $installationPath = (& $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($installationPath)) {
        throw 'Visual Studio with the x64 C++ toolchain is required.'
    }
    & (Join-Path $installationPath 'Common7\Tools\Launch-VsDevShell.ps1') `
        -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
    Push-Location $native
    try {
        cmake --build --preset windows-x64-debug --target `
            BeaconStreamWorkerNvencH264EncoderProbe
        if ($LASTEXITCODE -ne 0) {
            throw "HEVC Main10 probe build failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

$probe = Join-Path $BuildDirectory 'Beacon.StreamWorker.Tests\Debug\BeaconStreamWorkerNvencH264EncoderProbe.exe'
$tempRoot = 'D:\Temp'
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
$output = Join-Path $tempRoot ("beacon-nvenc-{0}.hevc" -f [guid]::NewGuid().ToString('N'))

try {
    if (-not (Test-Path -LiteralPath $probe -PathType Leaf)) {
        throw "Required NVENC probe is missing: $probe"
    }
    foreach ($tool in 'ffprobe', 'ffmpeg') {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            throw "$tool is required as an independent HEVC decoder oracle."
        }
    }

    $probeOutput = @(& $probe --hevc-main10 $output 2>&1)
    if ($LASTEXITCODE -ne 0 -or
        ($probeOutput -join "`n") -notmatch 'BEACON_NVENC_HEVC_MAIN10_OK') {
        throw "HEVC Main10 probe failed.`n$($probeOutput -join [Environment]::NewLine)"
    }
    $probeOutput | Write-Output

    $probeInfo = @(& ffprobe -v error -f hevc -select_streams v:0 `
        -show_entries stream=codec_name,profile,width,height,pix_fmt,has_b_frames,color_range,color_space,color_transfer,color_primaries `
        -of default=noprint_wrappers=1 $output 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "ffprobe rejected the Annex-B HEVC stream.`n$($probeInfo -join [Environment]::NewLine)"
    }
    $metadata = $probeInfo -join "`n"
    foreach ($required in @(
        'codec_name=hevc', 'profile=Main 10', 'width=640', 'height=400',
        'pix_fmt=yuv420p10le', 'has_b_frames=0', 'color_range=tv',
        'color_space=bt2020nc', 'color_transfer=smpte2084',
        'color_primaries=bt2020')) {
        if ($metadata -notmatch [regex]::Escape($required)) {
            throw "HEVC stream metadata is missing '$required'.`n$metadata"
        }
    }
    $probeInfo | Write-Output

    $decoded = @(& ffmpeg -v error -nostats -progress pipe:1 `
        -f hevc -i $output -f null NUL 2>&1)
    if ($LASTEXITCODE -ne 0 -or ($decoded -join "`n") -notmatch 'progress=end') {
        throw "ffmpeg failed to decode the HEVC stream.`n$($decoded -join [Environment]::NewLine)"
    }
    $frameMatch = $decoded | Select-String -Pattern '^frame=(?<value>\d+)$' |
        Select-Object -Last 1
    if (-not $frameMatch -or
        [uint32]$frameMatch.Matches[0].Groups['value'].Value -ne 4) {
        throw 'ffmpeg did not decode exactly four HEVC frames.'
    }

    $headers = @(& ffmpeg -loglevel trace -f hevc -i $output -c copy `
        -bsf:v trace_headers -f null NUL 2>&1)
    $headerText = $headers -join "`n"
    if ($LASTEXITCODE -ne 0 -or
        $headerText -notmatch '(?i)mastering.display.colour.volume' -or
        $headerText -notmatch '(?i)content.light.level') {
        throw 'Independent bitstream inspection did not find both HDR10 SEI messages.'
    }
    Write-Output 'BEACON_NVENC_HEVC_MAIN10_VALIDATION_OK frames=4 decoded=4 hdr10_sei=1'
}
finally {
    Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
}
