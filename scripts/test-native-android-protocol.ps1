[CmdletBinding()]
param(
    [string]$Serial = 'emulator-5554',
    [string]$WslDistribution = 'Ubuntu'
)

$ErrorActionPreference = 'Stop'
$wslHome = (& wsl.exe -d $WslDistribution -- bash -lc 'printf %s "$HOME"').Trim()
$androidBuild = "$wslHome/.cache/beacon/build/android-x86_64-debug"
$androidBuildWindows = (& wsl.exe -d $WslDistribution -- wslpath -w $androidBuild).Trim()
$remoteDirectory = '/data/local/tmp/beacon-native'
$tests = @(
    'BeaconStreamProtocolVersionTests',
    'BeaconMediaDatagramTests',
    'BeaconBenchmarkDatagramTests',
    'BeaconFakeTransportTests',
    'BeaconSessionTests',
    'BeaconFrameAssemblerTests',
    'BeaconServerSessionProtocolTests',
    'BeaconVideoMediaPacketizerTests',
    'BeaconAccessUnitVectorTests',
    'BeaconHostedEmulatorEndpointServerTests',
    'BeaconMsQuicTransportTests',
    'BeaconAndroidStreamCoreTests',
    'BeaconAndroidBenchmarkCollectorTests',
    'BeaconAndroidCertificatePinTests',
    'BeaconAndroidLifecycleTests'
)

& adb -s $Serial get-state | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Android target '$Serial' is not online."
}

& adb -s $Serial shell mkdir -p $remoteDirectory
& adb -s $Serial push `
    (Join-Path $androidBuildWindows 'msquic\bin\Debug\libmsquic.so') `
    "$remoteDirectory/libmsquic.so" | Out-Null

foreach ($test in $tests) {
    $localExecutable = Join-Path $androidBuildWindows "Beacon.StreamProtocol.Tests\$test"
    if (-not (Test-Path -LiteralPath $localExecutable)) {
        throw "Android native test '$test' is not built."
    }

    & adb -s $Serial push $localExecutable "$remoteDirectory/$test" | Out-Null
    & adb -s $Serial shell chmod 755 "$remoteDirectory/$test"
    & adb -s $Serial shell `
        "cd $remoteDirectory && export LD_LIBRARY_PATH=. && ./$test"
    if ($LASTEXITCODE -ne 0) {
        throw "Android native test '$test' failed."
    }
}

Write-Host 'BEACON_ANDROID_PROTOCOL_TESTS_OK'
