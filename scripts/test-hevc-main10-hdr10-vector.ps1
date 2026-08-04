[CmdletBinding()]
param(
    [string]$InputPath = (Join-Path $PSScriptRoot '..\src\Beacon.Android\app\src\main\assets\benchmark-vectors\beacon-hevc-main10-hdr10-320x180-30-v1.bau')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$resolvedInput = (Resolve-Path -LiteralPath $InputPath).Path
$bytes = [IO.File]::ReadAllBytes($resolvedInput)
$magic = [Text.Encoding]::ASCII.GetBytes("BEACONAU1`n")
if ($bytes.Length -lt ($magic.Length + 4)) {
    throw 'Access-unit vector is truncated.'
}
for ($index = 0; $index -lt $magic.Length; $index++) {
    if ($bytes[$index] -ne $magic[$index]) {
        throw 'Access-unit vector magic is invalid.'
    }
}

$offset = $magic.Length
$count = [BitConverter]::ToUInt32($bytes, $offset)
$offset += 4
if ($count -eq 0) {
    throw 'Access-unit vector contains no frames.'
}

$temporaryRoot = 'D:\Temp'
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
$reassembled = Join-Path $temporaryRoot ("beacon-hevc-hdr10-{0:N}.h265" -f [guid]::NewGuid())
$output = [IO.File]::Create($reassembled)
try {
    for ($frame = 0; $frame -lt $count; $frame++) {
        if (($offset + 4) -gt $bytes.Length) {
            throw "Frame $frame length is truncated."
        }
        $length = [BitConverter]::ToUInt32($bytes, $offset)
        $offset += 4
        if ($length -eq 0 -or ($offset + $length) -gt $bytes.Length) {
            throw "Frame $frame payload is invalid."
        }
        $output.Write($bytes, $offset, $length)
        $offset += $length
    }
    if ($offset -ne $bytes.Length) {
        throw 'Access-unit vector contains trailing bytes.'
    }
}
finally {
    $output.Dispose()
}

try {
    $stream = (& ffprobe.exe -v error -select_streams v:0 `
        -show_entries stream=codec_name,profile,pix_fmt,color_range,color_space,color_transfer,color_primaries `
        -of json $reassembled | ConvertFrom-Json).streams[0]
    $expected = @{
        codec_name = 'hevc'
        profile = 'Main 10'
        pix_fmt = 'yuv420p10le'
        color_range = 'tv'
        color_space = 'bt2020nc'
        color_transfer = 'smpte2084'
        color_primaries = 'bt2020'
    }
    foreach ($entry in $expected.GetEnumerator()) {
        if ($stream.($entry.Key) -ne $entry.Value) {
            throw "ffprobe $($entry.Key) was '$($stream.($entry.Key))'; expected '$($entry.Value)'."
        }
    }

    $frame = (& ffprobe.exe -v error -select_streams v:0 -show_frames `
        -read_intervals '%+#1' `
        -show_entries frame_side_data=side_data_type,max_content,max_average `
        -of json $reassembled | ConvertFrom-Json).frames[0]
    $sideData = @($frame.side_data_list)
    if (-not ($sideData.side_data_type -contains 'Mastering display metadata')) {
        throw 'Mastering-display SEI metadata is missing.'
    }
    $contentLight = $sideData | Where-Object side_data_type -eq 'Content light level metadata'
    if ($null -eq $contentLight -or $contentLight.max_content -ne 1000 -or $contentLight.max_average -ne 400) {
        throw 'Content-light SEI metadata is missing or incorrect.'
    }

    & ffmpeg.exe -v error -i $reassembled -f null -
    if ($LASTEXITCODE -ne 0) {
        throw 'FFmpeg could not decode the complete reassembled Main10 stream.'
    }

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedInput).Hash.ToLowerInvariant()
    Write-Output "BEACON_HEVC_MAIN10_HDR10_VECTOR_OK frames=$count sha256=$hash"
}
finally {
    Remove-Item -LiteralPath $reassembled -Force -ErrorAction SilentlyContinue
}
