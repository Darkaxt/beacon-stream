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
$retryIdentityPath = Join-Path ([IO.Path]::GetTempPath()) "beacon-quic-listener-retry-$([Guid]::NewGuid().ToString('N')).pfx"
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

    [IO.File]::WriteAllBytes($retryIdentityPath, [byte[]](1, 2, 3, 4))
    $output = & $probe $identityPath $fingerprint $retryIdentityPath
    $probeExitCode = $LASTEXITCODE
    if ($probeExitCode -ne 0 -or
        $output -notmatch '^BEACON_QUIC_LOOPBACK_OK 3 CERT_PIN_OK ALPN_VERSION_OK REPLAY_RECONNECT_OK CALLBACK_FAULTS_OK DISCONNECT_FAULTS_OK$') {
        throw "Beacon QUIC listener probe failed with exit code ${probeExitCode}: $output"
    }
    Write-Host $output
}
finally {
    Remove-Item -LiteralPath $identityPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $retryIdentityPath -Force -ErrorAction SilentlyContinue
}
