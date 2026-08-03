[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$nativeRoot = Join-Path $repositoryRoot 'native'

& (Join-Path $PSScriptRoot 'bootstrap-native-dependencies.ps1') `
    -Name msquic,xdp-for-windows,protobuf,abseil-cpp,opus
& (Join-Path $PSScriptRoot 'install-windows-app-sdk.ps1') | Out-Null

$env:BEACON_PROTOC_EXECUTABLE = (& (Join-Path $PSScriptRoot 'install-protoc.ps1')).Trim()
if (-not (Test-Path -LiteralPath $env:BEACON_PROTOC_EXECUTABLE)) {
    throw 'Pinned protoc 32.1 executable was not installed.'
}

$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = (& $vswhere `
    -latest `
    -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath).Trim()
if (-not $visualStudio) {
    throw 'Visual Studio C++ tools were not found.'
}

if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    $developerCommand = Join-Path $visualStudio 'Common7\Tools\VsDevCmd.bat'
    $environmentLines = & cmd.exe /s /c "`"$developerCommand`" -arch=x64 -host_arch=x64 >nul && set"
    foreach ($line in $environmentLines) {
        if ($line -match '^([^=]+)=(.*)$') {
            Set-Item -Path "Env:$($matches[1])" -Value $matches[2]
        }
    }
}

if (-not (Get-Command ninja.exe -ErrorAction SilentlyContinue)) {
    $visualStudioNinja = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja'
    if (-not (Test-Path -LiteralPath (Join-Path $visualStudioNinja 'ninja.exe'))) {
        throw 'Ninja was not found on PATH or in Visual Studio.'
    }
    $env:PATH = "$visualStudioNinja;$env:PATH"
}

$cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
if ($cmakeCommand) {
    $cmake = $cmakeCommand.Source
}
else {
    $cmake = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
}

if (-not (Test-Path -LiteralPath $cmake)) {
    throw 'CMake 3.25 or newer was not found on PATH or in Visual Studio.'
}

$ctest = Join-Path (Split-Path -Parent $cmake) 'ctest.exe'
Push-Location $nativeRoot
try {
    & $cmake --fresh --preset windows-x64
    if ($LASTEXITCODE -ne 0) { throw 'Windows native configure failed.' }
    & $cmake --build --preset windows-x64-debug
    if ($LASTEXITCODE -ne 0) { throw 'Windows native build failed.' }
    & $ctest --preset windows-x64-debug
    if ($LASTEXITCODE -ne 0) { throw 'Windows native tests failed.' }
}
finally {
    Pop-Location
}
