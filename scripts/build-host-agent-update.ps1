param(
    [Parameter(Mandatory = $true)]
    [string]$PayloadRoot,
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,
    [Parameter(Mandatory = $true)]
    [string]$PackageId,
    [Parameter(Mandatory = $true)]
    [string]$SourceCommit,
    [string]$MinimumBootstrapVersion = "1.0.0"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$packageProject = Join-Path $repositoryRoot "src\Beacon.HostAgent.Package\Beacon.HostAgent.Package.csproj"

& dotnet run --project $packageProject --configuration Release -- `
    build `
    --payload-root ([IO.Path]::GetFullPath($PayloadRoot)) `
    --package-root ([IO.Path]::GetFullPath($PackageRoot)) `
    --package-id $PackageId `
    --source-commit $SourceCommit `
    --minimum-bootstrap-version $MinimumBootstrapVersion
if ($LASTEXITCODE -ne 0) {
    throw "Host Agent update package build failed with exit code $LASTEXITCODE."
}

& dotnet run --project $packageProject --configuration Release --no-build -- `
    verify `
    --package-root ([IO.Path]::GetFullPath($PackageRoot)) `
    --bootstrap-version $MinimumBootstrapVersion
if ($LASTEXITCODE -ne 0) {
    throw "Host Agent update package verification failed with exit code $LASTEXITCODE."
}
