[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$probe = Join-Path $repositoryRoot `
    'native\out\build\windows-x64\Beacon.StreamWorker.Tests\Debug\BeaconStreamWorkerQuicListenerProbe.exe'
if (-not (Test-Path -LiteralPath $probe)) {
    throw 'Beacon QUIC listener probe is not built.'
}

$identityPath = Join-Path ([IO.Path]::GetTempPath()) "beacon-quic-listener-$([Guid]::NewGuid().ToString('N')).pfx"
try {
    $key = [Security.Cryptography.RSA]::Create(3072)
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=Beacon QUIC Listener Test',
            $key,
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $certificate = $request.CreateSelfSigned(
            [DateTimeOffset]::UtcNow.AddDays(-1),
            [DateTimeOffset]::UtcNow.AddDays(1))
        try {
            $pfx = $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx)
            try {
                [IO.File]::WriteAllBytes($identityPath, $pfx)
            }
            finally {
                [Security.Cryptography.CryptographicOperations]::ZeroMemory($pfx)
            }
        }
        finally {
            $certificate.Dispose()
        }
    }
    finally {
        $key.Dispose()
    }

    $output = & $probe $identityPath
    $probeExitCode = $LASTEXITCODE
    if ($probeExitCode -ne 0 -or $output -notmatch '^BEACON_QUIC_LISTENER_READY [1-9][0-9]*$') {
        throw "Beacon QUIC listener probe failed with exit code ${probeExitCode}: $output"
    }
    Write-Host $output
}
finally {
    Remove-Item -LiteralPath $identityPath -Force -ErrorAction SilentlyContinue
}
