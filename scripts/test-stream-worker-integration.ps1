[CmdletBinding()]
param(
    [string]$WorkerPath = ''
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
}
finally {
    Remove-Item Env:BEACON_SERVER_IDENTITY_PATH -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $identityPath -Force -ErrorAction SilentlyContinue
}
