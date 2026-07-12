[CmdletBinding()]
param(
    [string[]]$Tasks = @('test', 'assembleDebug', 'assembleDebugAndroidTest')
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$androidRoot = Join-Path $repositoryRoot 'src\Beacon.Android'
$gradleRoot = Join-Path $env:USERPROFILE '.gradle\wrapper\dists'
if (-not (Test-Path -LiteralPath $gradleRoot -PathType Container)) {
    throw 'No cached Gradle distribution directory is available.'
}

$gradleCandidates = @(
    Get-ChildItem -LiteralPath $gradleRoot -Recurse -File -Filter gradle.bat |
        Where-Object { $_.FullName -match 'gradle-8\.14\.1-bin' }
)
if ($gradleCandidates.Count -ne 1) {
    throw 'Expected exactly one cached Gradle 8.14.1 runtime.'
}

& $gradleCandidates[0].FullName --no-daemon -p $androidRoot @Tasks --console=plain
exit $LASTEXITCODE
