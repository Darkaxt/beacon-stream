[CmdletBinding()]
param(
    [string]$Serial = 'emulator-5554',
    [switch]$ValidateFixtures
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'gate3-validation-common.ps1')

function Invoke-Gate3StaticAbsence([string]$Root) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new('git')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
        '-C', $Root, 'ls-files', '-z', '--cached', '--others', '--exclude-standard')) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'Could not start git tracked-file scan.'
        }
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $tracked = $output.GetAwaiter().GetResult()
        $errorText = $errorOutput.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "git ls-files failed with exit code $($process.ExitCode): $errorText"
        }
    }
    finally {
        $process.Dispose()
    }

    $guardPath = Join-Path $Root 'contracts/gate3_architecture_guard.json'
    if (-not (Test-Path -LiteralPath $guardPath -PathType Leaf)) {
        throw 'Gate 3 architecture guard manifest is unavailable.'
    }
    $guard = Get-Content -LiteralPath $guardPath -Raw | ConvertFrom-Json
    $scannedRoots = @($guard.scannedRoots)
    $rootFiles = @($guard.scannedRootFiles)
    $extensions = @($guard.scannedExtensions)
    $excludedPaths = @($guard.excludedPaths)
    $literalTokens = @(
        $guard.literalTokenFragments | ForEach-Object { -join @($_) })
    $patternParts = @($literalTokens | ForEach-Object { [Regex]::Escape($_) })
    $patternParts += @(
        $guard.regexTokenFragments | ForEach-Object { -join @($_) })
    $compatibilityPattern = '(?i)' + ($patternParts -join '|')
    $violations = [Collections.Generic.List[string]]::new()
    foreach ($forbiddenDirectory in @(
        $guard.forbiddenDirectoryFragments | ForEach-Object { -join @($_) })) {
        if (Test-Path -LiteralPath (Join-Path $Root $forbiddenDirectory) -PathType Container) {
            $violations.Add($forbiddenDirectory)
        }
    }

    foreach ($relativePath in $tracked.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)) {
        $normalized = $relativePath.Replace('\', '/')
        $firstSegment = $normalized.Split('/', 2)[0]
        $inScannedTree = $scannedRoots -contains $firstSegment
        $scannedRootFile = -not $normalized.Contains('/') -and $rootFiles -contains $normalized
        $extension = [IO.Path]::GetExtension($normalized)
        if ((-not $inScannedTree -and -not $scannedRootFile) -or $extensions -notcontains $extension) {
            continue
        }
        if ($excludedPaths -contains $normalized -or
            $normalized -match '(?i)(?:^|/)(?:bin|obj|build|coverage|dist|node_modules|out|_deps|\.cxx|\.gradle)(?:/|$)') {
            continue
        }

        $path = Join-Path $Root $normalized.Replace('/', [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            continue
        }
        $contents = Get-Content -LiteralPath $path -Raw
        if ($normalized -match $compatibilityPattern -or $contents -match $compatibilityPattern) {
            $violations.Add($normalized)
        }
    }

    if ($violations.Count -ne 0) {
        throw "Gate 3 prohibited routes remain in tracked files: $($violations -join ', ')"
    }
    Write-Output 'BEACON_GATE3_STATIC_ABSENCE_OK'
}

function Assert-LastExitCode([string]$Operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Invoke-AndroidInstrumentationSuite([string]$Root, [string]$AndroidSerial) {
    $appApk = Join-Path $Root 'src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk'
    $testApk = Join-Path $Root `
        'src\Beacon.Android\app\build\outputs\apk\androidTest\debug\app-debug-androidTest.apk'
    foreach ($apk in @($appApk, $testApk)) {
        if (-not (Test-Path -LiteralPath $apk -PathType Leaf)) {
            throw 'Android instrumentation APK is unavailable.'
        }
        & adb -s $AndroidSerial install -r $apk | Out-Null
        Assert-LastExitCode 'Android instrumentation APK install'
    }

    $output = (& adb -s $AndroidSerial shell am instrument -w -r `
        dev.beacon.android.test/androidx.test.runner.AndroidJUnitRunner 2>&1) -join `
        [Environment]::NewLine
    Assert-LastExitCode 'Android instrumentation process'
    if (-not (Test-Gate3AndroidInstrumentationSucceeded $LASTEXITCODE $output)) {
        throw "Android instrumentation suite failed.`n$output"
    }
    Write-Output $output
}

function Invoke-Gate3Validation([string]$Root, [string]$AndroidSerial) {
    $completedStages = [Collections.Generic.List[string]]::new()
    $evidenceRoot = Join-Path (
        [IO.Path]::GetTempPath()) "beacon-gate3-evidence-$([Guid]::NewGuid().ToString('N'))"

    function Complete-Stage([string]$Name) {
        $completedStages.Add($Name)
    }

    try {
        Push-Location $Root
        try {
            & dotnet restore Beacon.slnx
            Assert-LastExitCode '.NET restore'
            & dotnet format Beacon.slnx --verify-no-changes --no-restore
            Assert-LastExitCode '.NET format'
            & dotnet build Beacon.slnx -warnaserror --no-restore
            Assert-LastExitCode '.NET build'
            & dotnet test Beacon.slnx --no-build
            Assert-LastExitCode '.NET tests'
            Complete-Stage 'dotnet'

            & (Join-Path $PSScriptRoot 'build-native-windows.ps1')
            Assert-LastExitCode 'Windows native build and CTest'
            & (Join-Path $PSScriptRoot 'test-stream-worker-integration.ps1')
            Assert-LastExitCode 'StreamWorker process integration'
            Complete-Stage 'native-windows'

            & (Join-Path $PSScriptRoot 'test-android.ps1') `
                -Tasks @('clean', 'test', 'assembleDebug', 'assembleRelease', 'assembleDebugAndroidTest')
            Assert-LastExitCode 'Android clean, unit, and package builds'
            & (Join-Path $PSScriptRoot 'build-native-android-wsl.ps1')
            Assert-LastExitCode 'Android native builds'
            Complete-Stage 'android-build'

            & (Join-Path $PSScriptRoot 'test-native-android-protocol.ps1') -Serial $AndroidSerial
            Assert-LastExitCode 'Android native protocol tests'
            Invoke-AndroidInstrumentationSuite $Root $AndroidSerial
            Complete-Stage 'android-tests'

            & pnpm --dir src/Beacon.ClientLab install --frozen-lockfile
            Assert-LastExitCode 'Client Lab install'
            & pnpm --dir src/Beacon.ClientLab lint
            Assert-LastExitCode 'Client Lab lint'
            & pnpm --dir src/Beacon.ClientLab test
            Assert-LastExitCode 'Client Lab tests'
            & pnpm --dir tests/Beacon.ClientLab.Playwright install --frozen-lockfile
            Assert-LastExitCode 'Client Lab Playwright install'
            & pnpm --dir tests/Beacon.ClientLab.Playwright lint
            Assert-LastExitCode 'Client Lab Playwright lint'
            & pnpm --dir tests/Beacon.ClientLab.Playwright test
            Assert-LastExitCode 'Client Lab Playwright tests'
            Complete-Stage 'client-lab'

            & dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj `
                --no-build --filter FakeEndpointLiveTests
            Assert-LastExitCode 'FakeEndpoint live process flow'
            Complete-Stage 'fake-endpoint-live'

            & dotnet run --project src/Beacon.DisplayProbe --no-build -- status
            Assert-LastExitCode 'DisplayProbe status'
            Complete-Stage 'display-probe'

            & dotnet run --project src/Beacon.GameProbe --no-build -- scan --json
            Assert-LastExitCode 'GameProbe scan'
            Complete-Stage 'game-probe'

            & (Join-Path $PSScriptRoot 'test-gate3-emulator-session.ps1') `
                -Serial $AndroidSerial -ArtifactsReady -EvidenceDirectory $evidenceRoot
            Assert-LastExitCode 'Gate 3 real emulator session'
            Complete-Stage 'real-emulator-session'

            Invoke-Gate3StaticAbsence $Root
            Complete-Stage 'static-absence'

            & (Join-Path $PSScriptRoot 'test-gate3-secret-absence.ps1') `
                -EvidenceDirectory $evidenceRoot `
                -CanaryManifest (Join-Path $evidenceRoot 'canaries.json')
            Assert-LastExitCode 'Gate 3 secret absence'
            Complete-Stage 'secret-absence'
        }
        finally {
            Pop-Location
        }

        [PSCustomObject]@{
            status = 'passed'
            gate = 3
            serial = $AndroidSerial
            stageCount = $completedStages.Count
            stages = @($completedStages)
        } | ConvertTo-Json -Compress | Write-Output
    }
    finally {
        $resolvedEvidence = [IO.Path]::GetFullPath($evidenceRoot)
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ($resolvedEvidence.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $resolvedEvidence)) {
            Remove-Item -LiteralPath $resolvedEvidence -Recurse -Force
        }
    }
}

if ($ValidateFixtures) {
    Invoke-Gate3StaticAbsence $repositoryRoot
    & (Join-Path $PSScriptRoot 'test-gate3-secret-absence.ps1') -ValidateFixtures
    if ($LASTEXITCODE -ne 0) { throw 'Gate 3 secret fixtures failed.' }
    & (Join-Path $PSScriptRoot 'test-gate3-emulator-session.ps1') -ValidateKestrelParser
    if ($LASTEXITCODE -ne 0) { throw 'Gate 3 runner fixtures failed.' }
    Write-Output 'BEACON_GATE3_FIXTURES_OK'
    exit 0
}

Invoke-Gate3Validation $repositoryRoot $Serial
