[CmdletBinding()]
param(
    [string]$Distribution = 'Ubuntu',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$portableRepositoryRoot = $repositoryRoot.Replace('\', '/')
$translatedPath = & wsl.exe -d $Distribution -- wslpath -a $portableRepositoryRoot
$wslRepositoryRoot = ($translatedPath | Out-String).Trim()
if (-not $wslRepositoryRoot) {
    throw "Could not translate the repository path for WSL distribution '$Distribution'."
}

& wsl.exe -d $Distribution -- bash "$wslRepositoryRoot/scripts/build-native-android-wsl.sh" $Configuration
if ($LASTEXITCODE -ne 0) {
    throw 'Android native build failed in WSL.'
}
