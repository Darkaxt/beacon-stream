[CmdletBinding()]
param(
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$lock = Get-Content -LiteralPath (Join-Path $repositoryRoot 'native\dependencies.lock.json') -Raw | ConvertFrom-Json
$toolchain = $lock.toolchains.protobufCompiler

if (-not $Destination) {
    $Destination = Join-Path $repositoryRoot "native\_tools\protoc-$($toolchain.version)-windows-x64"
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$archive = Join-Path $Destination "protoc-$($toolchain.version)-win64.zip"
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri $toolchain.windowsArchive -OutFile $archive
}

$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $toolchain.windowsSha256) {
    throw "Pinned protoc archive hash '$actualHash' does not match '$($toolchain.windowsSha256)'."
}

$executable = Join-Path $Destination 'bin\protoc.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    Expand-Archive -LiteralPath $archive -DestinationPath $Destination -Force
}

$actualVersion = (& $executable --version).Trim()
if ($actualVersion -ne "libprotoc $($toolchain.version)") {
    throw "Pinned protoc reported '$actualVersion'."
}

$executable
