[CmdletBinding()]
param(
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $repositoryRoot 'native\dependencies.lock.json'
$dependency = (Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json).toolchains.windowsAppSdk

if (-not $Destination) {
    $Destination = Join-Path $repositoryRoot 'native\_deps\windows-app-sdk'
}
$Destination = [System.IO.Path]::GetFullPath($Destination)

$packages = @(
    @{
        Name = 'foundation'
        ExpectedId = 'Microsoft.WindowsAppSDK.Foundation'
        Nuspec = 'Microsoft.WindowsAppSDK.Foundation.nuspec'
        Version = $dependency.foundationVersion
        Archive = $dependency.foundationArchive
        Sha256 = $dependency.foundationSha256
        Required = 'include\MddBootstrap.h'
    },
    @{
        Name = 'interactive-experiences'
        ExpectedId = 'Microsoft.WindowsAppSDK.InteractiveExperiences'
        Nuspec = 'Microsoft.WindowsAppSDK.InteractiveExperiences.nuspec'
        Version = $dependency.interactiveExperiencesVersion
        Archive = $dependency.interactiveExperiencesArchive
        Sha256 = $dependency.interactiveExperiencesSha256
        Required = 'include\Microsoft.UI.Interop.h'
    },
    @{
        Name = 'runtime'
        ExpectedId = 'Microsoft.WindowsAppSDK.Runtime'
        Nuspec = 'Microsoft.WindowsAppSDK.Runtime.nuspec'
        Version = $dependency.runtimeVersion
        Archive = $dependency.runtimeArchive
        Sha256 = $dependency.runtimeSha256
        Required = 'include\WindowsAppSDK-VersionInfo.h'
    }
)

function Test-WindowsAppSdkPackage {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [hashtable]$Package
    )

    $required = Join-Path $Root $Package.Required
    $nuspecPath = Join-Path $Root $Package.Nuspec
    if (-not (Test-Path -LiteralPath $required -PathType Leaf) -or
        -not (Test-Path -LiteralPath $nuspecPath -PathType Leaf)) {
        return $false
    }

    try {
        [xml]$nuspec = Get-Content -LiteralPath $nuspecPath -Raw
        return $nuspec.package.metadata.id -eq $Package.ExpectedId -and
            $nuspec.package.metadata.version -eq $Package.Version
    }
    catch {
        return $false
    }
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
foreach ($package in $packages) {
    $target = Join-Path $Destination $package.Name
    if (Test-WindowsAppSdkPackage -Root $target -Package $package) {
        continue
    }
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }

    $staging = Join-Path $Destination ('.install-' + $package.Name + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null
    try {
        $archive = Join-Path $staging ($package.Name + '.nupkg')
        Invoke-WebRequest -Uri $package.Archive -OutFile $archive
        $actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $package.Sha256) {
            throw "Windows App SDK package '$($package.Name)' hash '$actualHash' did not match '$($package.Sha256)'."
        }

        $zip = Join-Path $staging ($package.Name + '.zip')
        Copy-Item -LiteralPath $archive -Destination $zip
        $expanded = Join-Path $staging 'package'
        Expand-Archive -LiteralPath $zip -DestinationPath $expanded
        if (-not (Test-WindowsAppSdkPackage -Root $expanded -Package $package)) {
            throw "Windows App SDK package '$($package.Name)' did not match its pinned identity and version."
        }
        Move-Item -LiteralPath $expanded -Destination $target
    }
    finally {
        if (Test-Path -LiteralPath $staging) {
            Remove-Item -LiteralPath $staging -Recurse -Force
        }
    }
}

$interactiveVersion = [string]$dependency.interactiveExperiencesVersion
$projectionHeader = Join-Path $Destination 'projections\winrt\Microsoft.UI.h'
$projectionRoot = Join-Path $Destination 'projections'
$projectionVersionPath = Join-Path $projectionRoot '.projection-version'
$kitsBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$cppwinrt = Get-ChildItem -LiteralPath $kitsBin -Directory |
    Where-Object { $_.Name -match '^10\.' } |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64\cppwinrt.exe' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $cppwinrt) {
    throw 'Windows SDK cppwinrt.exe was not found.'
}

$metadataRoot = Get-ChildItem -LiteralPath (Join-Path $Destination 'interactive-experiences\metadata') -Directory |
    Where-Object { $_.Name -match '^10\.' } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1
if (-not $metadataRoot) {
    throw 'Windows App SDK Interactive Experiences metadata was not found.'
}
$windowsSdkVersion = Split-Path (Split-Path (Split-Path $cppwinrt -Parent) -Parent) -Leaf
$projectionVersion = "interactive=$interactiveVersion;metadata=$($metadataRoot.Name);windows-sdk=$windowsSdkVersion"
$projectionReady = (Test-Path -LiteralPath $projectionHeader -PathType Leaf) -and
    (Test-Path -LiteralPath $projectionVersionPath -PathType Leaf) -and
    ((Get-Content -LiteralPath $projectionVersionPath -Raw).Trim() -eq $projectionVersion)

if (-not $projectionReady) {
    $projectionStaging = Join-Path $Destination ('.projection-' + [guid]::NewGuid().ToString('N'))
    try {
        & $cppwinrt `
            -input (Join-Path $metadataRoot.FullName 'Microsoft.UI.winmd') `
            -reference sdk `
            -reference $metadataRoot.FullName `
            -output $projectionStaging
        $stagedHeader = Join-Path $projectionStaging 'winrt\Microsoft.UI.h'
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $stagedHeader -PathType Leaf)) {
            throw 'Windows App SDK C++/WinRT projection generation failed.'
        }
        $projectionVersion | Set-Content -LiteralPath (Join-Path $projectionStaging '.projection-version') -NoNewline
        if (Test-Path -LiteralPath $projectionRoot) {
            Remove-Item -LiteralPath $projectionRoot -Recurse -Force
        }
        Move-Item -LiteralPath $projectionStaging -Destination $projectionRoot
    }
    finally {
        if (Test-Path -LiteralPath $projectionStaging) {
            Remove-Item -LiteralPath $projectionStaging -Recurse -Force
        }
    }
}

Write-Output $Destination
