[CmdletBinding()]
param(
    [string]$WorkerPath = '',
    [switch]$AllowUnsupportedVideoHardware
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
$identityPath = Join-Path ([IO.Path]::GetTempPath()) "beacon-worker-identity-$([Guid]::NewGuid().ToString('N')).pfx"
$testProject = Join-Path $repositoryRoot `
    'tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj'
$nativeProbe = Join-Path $repositoryRoot `
    'native\out\build\windows-x64\Beacon.StreamWorker.Tests\Debug\BeaconStreamWorkerQuicListenerProbe.exe'
if (-not (Test-Path -LiteralPath $nativeProbe -PathType Leaf)) {
    throw "Beacon native Worker process probe was not found at '$nativeProbe'."
}
Add-Type -AssemblyName System.Windows.Forms
$primaryScreen = [System.Windows.Forms.Screen]::PrimaryScreen
if ($null -eq $primaryScreen) {
    throw 'No primary Windows display is available for the production capture probe.'
}
$displayDevice = $primaryScreen.DeviceName
$displayWidth = $primaryScreen.Bounds.Width
$displayHeight = $primaryScreen.Bounds.Height
try {
    $key = [Security.Cryptography.RSA]::Create(3072)
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=Beacon Worker Integration',
            $key,
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $certificate = $request.CreateSelfSigned(
            [DateTimeOffset]::UtcNow.AddDays(-1),
            [DateTimeOffset]::UtcNow.AddDays(1))
        try {
            $publicKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey(
                $certificate)
            try {
                $spki = $publicKey.ExportSubjectPublicKeyInfo()
                try {
                    $fingerprintBytes = [Security.Cryptography.SHA256]::HashData($spki)
                    try { $fingerprint = [Convert]::ToHexString($fingerprintBytes) }
                    finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($fingerprintBytes) }
                }
                finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($spki) }
            }
            finally { $publicKey.Dispose() }
            $pfx = $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx)
            try { [IO.File]::WriteAllBytes($identityPath, $pfx) }
            finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($pfx) }
        }
        finally { $certificate.Dispose() }
    }
    finally { $key.Dispose() }

    $env:BEACON_SERVER_IDENTITY_PATH = $identityPath
    & dotnet restore $testProject
    if ($LASTEXITCODE -ne 0) { throw 'StreamWorker integration restore failed.' }
    & dotnet test $testProject `
        --no-restore `
        --filter 'FullyQualifiedName~RealWorkerCompletesExplicitLifecycleWhenBinaryIsAvailable'
    if ($LASTEXITCODE -ne 0) { throw 'StreamWorker process integration failed.' }

    $nativeOutput = & $nativeProbe `
        --worker $WorkerPath `
        --identity $identityPath `
        --fingerprint $fingerprint `
        --display $displayDevice `
        --width $displayWidth `
        --height $displayHeight
    $nativeExitCode = $LASTEXITCODE
    if ($nativeExitCode -eq 0 -and
        $nativeOutput -match '^BEACON_WORKER_IPC_QUIC_OK AUTH INPUT FEEDBACK REAL_H264_ACCESS_UNIT DISCONNECT SHUTDOWN$') {
        Write-Host $nativeOutput
    }
    elseif ($AllowUnsupportedVideoHardware -and
        $nativeExitCode -eq 99 -and
        $nativeOutput -eq 'BEACON_WORKER_VIDEO_FAILURE CAPTURE 5') {
        Write-Host 'BEACON_WORKER_VIDEO_UNAVAILABLE NVIDIA_ADAPTER_MISSING'
    }
    else {
        throw "Native Worker IPC/QUIC integration failed with exit code ${nativeExitCode}: $nativeOutput"
    }

    $failureOutput = & $nativeProbe `
        --worker $WorkerPath `
        --identity $identityPath `
        --fingerprint $fingerprint `
        --display '\\.\BEACON-NOT-A-DISPLAY' `
        --width 2560 `
        --height 1600
    $failureExitCode = $LASTEXITCODE
    if ($failureExitCode -ne 99 -or
        $failureOutput -ne 'BEACON_WORKER_VIDEO_FAILURE CAPTURE 2') {
        throw "Native Worker failure diagnostic integration failed with exit code ${failureExitCode}: $failureOutput"
    }
    Write-Host $failureOutput

    $benchmarkOutput = & $nativeProbe `
        --benchmark-worker $WorkerPath `
        --identity $identityPath `
        --fingerprint $fingerprint
    $benchmarkExitCode = $LASTEXITCODE
    if ($benchmarkExitCode -ne 0 -or
        $benchmarkOutput -notmatch '^BEACON_WORKER_BENCHMARK_OK AUTH RELIABLE DATAGRAM RTT DISCONNECT SHUTDOWN$') {
        throw "Native Worker benchmark integration failed with exit code ${benchmarkExitCode}: $benchmarkOutput"
    }
    Write-Host $benchmarkOutput

    $startupExitOutput = & $nativeProbe `
        --worker $nativeProbe `
        --identity $identityPath `
        --fingerprint $fingerprint `
        --display $displayDevice `
        --width $displayWidth `
        --height $displayHeight
    $startupExitCode = $LASTEXITCODE
    if ($startupExitCode -ne 97 -or
        $startupExitOutput -notmatch '^BEACON_WORKER_STARTUP_EXIT 64$') {
        throw "Native Worker startup-exit proof failed with exit code ${startupExitCode}: $startupExitOutput"
    }
    Write-Host $startupExitOutput
}
finally {
    Remove-Item Env:BEACON_SERVER_IDENTITY_PATH -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $identityPath -Force -ErrorAction SilentlyContinue
}

exit 0
