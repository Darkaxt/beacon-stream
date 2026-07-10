[CmdletBinding()]
param(
    [string]$Destination,
    [string[]]$Name
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $repositoryRoot 'native\dependencies.lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json

if (-not $Destination) {
    $Destination = Join-Path $repositoryRoot 'native\_deps'
}

$selected = if ($Name) { $Name } else { @($lock.dependencies.PSObject.Properties.Name) }
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

foreach ($dependencyName in $selected) {
    $property = $lock.dependencies.PSObject.Properties[$dependencyName]
    if (-not $property) {
        throw "Unknown native dependency '$dependencyName'."
    }

    $dependency = $property.Value
    $target = Join-Path $Destination $dependency.path
    if (-not (Test-Path -LiteralPath (Join-Path $target '.git'))) {
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        & git -C $target init --quiet
        & git -C $target remote add origin $dependency.repository
    }

    $remote = (& git -C $target remote get-url origin).Trim()
    if ($remote -ne $dependency.repository) {
        throw "Dependency '$dependencyName' has unexpected remote '$remote'."
    }

    & git -C $target fetch --quiet --depth 1 origin $dependency.ref
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to fetch '$dependencyName' at '$($dependency.ref)'."
    }

    & git -C $target checkout --quiet --detach $dependency.revision
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to check out '$dependencyName' at '$($dependency.revision)'."
    }

    $actual = (& git -C $target rev-parse HEAD).Trim()
    if ($actual -ne $dependency.revision) {
        throw "Dependency '$dependencyName' resolved to '$actual', expected '$($dependency.revision)'."
    }

    Write-Host "$dependencyName $actual"
}
