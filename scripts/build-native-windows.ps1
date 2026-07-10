[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$nativeRoot = Join-Path $repositoryRoot 'native'

& (Join-Path $PSScriptRoot 'bootstrap-native-dependencies.ps1') -Name msquic,xdp-for-windows

$cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
if ($cmakeCommand) {
    $cmake = $cmakeCommand.Source
}
else {
    $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    $visualStudio = (& $vswhere `
        -latest `
        -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath).Trim()
    $cmake = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
}

if (-not (Test-Path -LiteralPath $cmake)) {
    throw 'CMake 3.25 or newer was not found on PATH or in Visual Studio.'
}

$ctest = Join-Path (Split-Path -Parent $cmake) 'ctest.exe'
Push-Location $nativeRoot
try {
    & $cmake --preset windows-x64
    if ($LASTEXITCODE -ne 0) { throw 'Windows native configure failed.' }
    & $cmake --build --preset windows-x64-debug
    if ($LASTEXITCODE -ne 0) { throw 'Windows native build failed.' }
    & $ctest --preset windows-x64-debug
    if ($LASTEXITCODE -ne 0) { throw 'Windows native tests failed.' }
}
finally {
    Pop-Location
}
