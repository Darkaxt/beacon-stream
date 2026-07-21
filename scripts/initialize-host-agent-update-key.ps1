param(
    [string]$Repository = "Darkaxt/beacon-stream"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publicKeyPath = Join-Path $repositoryRoot `
    "src\Beacon.HostAgent.Update\Resources\host-agent-update-public.pem"
if (Test-Path -LiteralPath $publicKeyPath) {
    throw "The Host Agent update public key already exists. Key rotation requires a reviewed bootstrap migration."
}

$key = [Security.Cryptography.ECDsa]::Create(
    [Security.Cryptography.ECCurve+NamedCurves]::nistP256)
try {
    $privatePem = $key.ExportPkcs8PrivateKeyPem()
    $encodedPrivate = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($privatePem))
    $encodedPrivate | gh secret set `
        BEACON_HOST_AGENT_UPDATE_SIGNING_KEY_PEM_B64 `
        --repo $Repository
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub Host Agent signing secret initialization failed with exit code $LASTEXITCODE."
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($publicKeyPath)) | Out-Null
    [IO.File]::WriteAllText($publicKeyPath, $key.ExportSubjectPublicKeyInfoPem())
}
finally {
    $privatePem = $null
    $encodedPrivate = $null
    $key.Dispose()
    [GC]::Collect()
}

Write-Output "Initialized the GitHub signing secret and wrote only the public key."
