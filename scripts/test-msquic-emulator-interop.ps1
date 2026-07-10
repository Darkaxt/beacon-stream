[CmdletBinding()]
param(
    [string]$Serial = 'emulator-5554',
    [string]$WslDistribution = 'Ubuntu',
    [int]$Port = 45999
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$windowsBuild = Join-Path $repositoryRoot 'native\out\build\windows-x64\Beacon.StreamProtocol.Tests\Debug'
$serverExecutable = Join-Path $windowsBuild 'BeaconMsQuicInteropProof.exe'
$wslHome = (& wsl.exe -d $WslDistribution -- bash -lc 'printf %s "$HOME"').Trim()
$androidBuild = "$wslHome/.cache/beacon/build/android-x86_64"
$androidBuildWindows = (& wsl.exe -d $WslDistribution -- wslpath -w $androidBuild).Trim()

if (-not (Test-Path -LiteralPath $serverExecutable)) {
    throw 'Windows interop proof is not built.'
}

& adb -s $Serial get-state | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Android target '$Serial' is not online."
}

& adb -s $Serial shell mkdir -p /data/local/tmp/beacon-native
& adb -s $Serial push `
    (Join-Path $androidBuildWindows 'msquic\bin\Debug\libmsquic.so') `
    /data/local/tmp/beacon-native/libmsquic.so | Out-Null
& adb -s $Serial push `
    (Join-Path $androidBuildWindows 'Beacon.StreamProtocol.Tests\BeaconMsQuicInteropProof') `
    /data/local/tmp/beacon-native/BeaconMsQuicInteropProof | Out-Null
& adb -s $Serial shell chmod 755 /data/local/tmp/beacon-native/BeaconMsQuicInteropProof

$certificate = New-SelfSignedCertificate `
    -DnsName 'beacon-msquic-proof' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -NotAfter (Get-Date).AddDays(1)

try {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $serverExecutable
    $startInfo.WorkingDirectory = $windowsBuild
    $startInfo.ArgumentList.Add('server')
    $startInfo.ArgumentList.Add($Port.ToString([Globalization.CultureInfo]::InvariantCulture))
    $startInfo.ArgumentList.Add($certificate.Thumbprint)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $server = [Diagnostics.Process]::new()
    $server.StartInfo = $startInfo
    [void]$server.Start()
    $ready = $server.StandardOutput.ReadLine()
    if ($ready -ne 'BEACON_QUIC_READY') {
        $errorText = $server.StandardError.ReadToEnd()
        throw "Server did not become ready. stdout='$ready' stderr='$errorText'"
    }

    $clientOutput = & adb -s $Serial shell `
        "cd /data/local/tmp/beacon-native && export LD_LIBRARY_PATH=. && ./BeaconMsQuicInteropProof client 10.0.2.2 $Port"
    $clientCode = $LASTEXITCODE
    $server.WaitForExit()
    $serverOutput = $server.StandardOutput.ReadToEnd().Trim()
    $serverError = $server.StandardError.ReadToEnd().Trim()

    if ($clientCode -ne 0 -or $clientOutput -notcontains 'BEACON_QUIC_CLIENT_OK') {
        throw "Android MsQuic proof failed: $($clientOutput -join [Environment]::NewLine)"
    }
    if ($server.ExitCode -ne 0 -or $serverOutput -ne 'BEACON_QUIC_SERVER_OK') {
        throw "Windows MsQuic proof failed: stdout='$serverOutput' stderr='$serverError'"
    }

    Write-Host 'BEACON_MSQUIC_EMULATOR_INTEROP_OK'
}
finally {
    Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
}
