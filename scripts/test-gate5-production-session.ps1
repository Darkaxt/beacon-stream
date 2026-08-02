[CmdletBinding()]
param(
    [string]$Serial = 'emulator-5554',
    [string]$EvidenceDirectory,
    [string]$BeforeConnectSignalPath,
    [switch]$ArtifactsReady
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $ArtifactsReady) {
    & dotnet build (Join-Path $repositoryRoot 'src\Beacon.Server\Beacon.Server.csproj') `
        --configuration Debug --warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Beacon Server build failed.' }
    & dotnet build (Join-Path $repositoryRoot 'tests\Beacon.SessionProbe\Beacon.SessionProbe.csproj') `
        --configuration Debug --warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Beacon SessionProbe build failed.' }
    & dotnet build (Join-Path $repositoryRoot 'tests\Beacon.ProductionAcceptance\Beacon.ProductionAcceptance.csproj') `
        --configuration Debug --warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Beacon production acceptance runner build failed.' }
    & dotnet build (Join-Path $repositoryRoot 'src\Beacon.DisplayProbe\Beacon.DisplayProbe.csproj') `
        --configuration Release --warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Beacon DisplayProbe build failed.' }
    & dotnet build (Join-Path $repositoryRoot 'src\Beacon.HostAgent.Control\Beacon.HostAgent.Control.csproj') `
        --configuration Release --warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Beacon HostAgent Control build failed.' }
    & (Join-Path $repositoryRoot 'scripts\build-native-windows.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Beacon StreamWorker build failed.' }
    & (Join-Path $repositoryRoot 'scripts\test-android.ps1') `
        -Tasks @('assembleDebug', 'assembleDebugAndroidTest')
    if ($LASTEXITCODE -ne 0) { throw 'Beacon Android APK build failed.' }
}

$runId = [Guid]::NewGuid().ToString('N')
$clientId = "gate5-emulator-$runId"
$effectiveEvidenceDirectory = if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    Join-Path $repositoryRoot ".artifacts\gate5-production-$runId"
} else {
    [System.IO.Path]::GetFullPath($EvidenceDirectory)
}
[System.IO.Directory]::CreateDirectory($effectiveEvidenceDirectory) | Out-Null

$arguments = @(
    'run',
    '--project',
    (Join-Path $repositoryRoot 'tests\Beacon.ProductionAcceptance\Beacon.ProductionAcceptance.csproj'),
    '--no-build',
    '--',
    '--repository-root',
    $repositoryRoot,
    '--run-id',
    $runId,
    '--serial',
    $Serial,
    '--evidence-directory',
    $effectiveEvidenceDirectory
)
if (-not [string]::IsNullOrWhiteSpace($BeforeConnectSignalPath)) {
    $arguments += @('--before-connect-signal', $BeforeConnectSignalPath)
}

$testFailure = $null
$cleanupFailure = $null
try {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Beacon Gate 5 production acceptance failed.'
    }
} catch {
    $testFailure = $_
} finally {
    Write-Output 'BEACON_MANDATORY_POST_TEST_RESTORE_BEGIN'
    $guardPidPath = Join-Path $effectiveEvidenceDirectory 'display-guard.pid'
    if (Test-Path -LiteralPath $guardPidPath) {
        $guardPidValue = (Get-Content -LiteralPath $guardPidPath -Raw).Trim()
        $guardProcessId = 0
        if ([int]::TryParse($guardPidValue, [ref]$guardProcessId)) {
            $guardProcess = Get-Process -Id $guardProcessId -ErrorAction SilentlyContinue
            if ($null -ne $guardProcess) {
                $guardProcess.WaitForExit()
            }
        }
    }

    $displaySwitchProcess = Start-Process `
        -FilePath (Join-Path $env:WINDIR 'System32\DisplaySwitch.exe') `
        -ArgumentList '/internal' `
        -PassThru `
        -Wait `
        -WindowStyle Hidden
    $displaySwitchExit = $displaySwitchProcess.ExitCode
    $displaySwitchProcess.Dispose()
    & dotnet run --project (Join-Path $repositoryRoot 'src\Beacon.DisplayProbe') `
        --configuration Release --no-build -- restore-physical
    $restoreBeforeExit = $LASTEXITCODE
    $removeOutput = (& dotnet run --project (Join-Path $repositoryRoot 'src\Beacon.DisplayProbe') `
        --configuration Release --no-build -- remove --client $clientId | Out-String).Trim()
    $removeExit = $LASTEXITCODE
    Write-Output $removeOutput
    & dotnet run --project (Join-Path $repositoryRoot 'src\Beacon.DisplayProbe') `
        --configuration Release --no-build -- restore-physical
    $restoreAfterExit = $LASTEXITCODE
    $statusOutput = (& dotnet run --project (Join-Path $repositoryRoot 'src\Beacon.DisplayProbe') `
        --configuration Release --no-build -- status | Out-String).Trim()
    $statusExit = $LASTEXITCODE
    Write-Output $statusOutput
    $agentOutput = (& dotnet run --project (Join-Path $repositoryRoot 'src\Beacon.HostAgent.Control') `
        --configuration Release --no-build -- status | Out-String).Trim()
    $agentExit = $LASTEXITCODE
    Write-Output $agentOutput

    $topologyVerified =
        $statusExit -eq 0 -and
        $statusOutput.Contains('mirrorMode=False', [StringComparison]::Ordinal) -and
        $statusOutput.Contains('physicalPrimaryVerified=True', [StringComparison]::Ordinal) -and
        $statusOutput.Contains('kind=Physical', [StringComparison]::Ordinal) -and
        -not $statusOutput.Contains('kind=Virtual', [StringComparison]::Ordinal)
    $agentVerified = $false
    if ($agentExit -eq 0) {
        try {
            $agentStatus = $agentOutput | ConvertFrom-Json -Depth 16
            $agentVerified =
                $agentStatus.success -eq $true -and
                $agentStatus.payload.lease.leaseCount -eq 0 -and
                $agentStatus.payload.lease.heartbeatActive -eq $false
        } catch {
            $agentVerified = $false
        }
    }
    $removeVerified =
        $removeExit -eq 0 -or
        ($removeExit -eq 2 -and
            $removeOutput.StartsWith(
                "remove: failed: No active SudoVDA driver lease owns client-",
                [StringComparison]::Ordinal))
    if ($displaySwitchExit -ne 0 -or
        $restoreBeforeExit -ne 0 -or
        -not $removeVerified -or
        $restoreAfterExit -ne 0 -or
        -not $topologyVerified -or
        -not $agentVerified) {
        $cleanupFailure =
            "Gate 5 mandatory cleanup was not verified. " +
            "displaySwitch=$displaySwitchExit restoreBefore=$restoreBeforeExit remove=$removeExit " +
            "restoreAfter=$restoreAfterExit topology=$topologyVerified agent=$agentVerified"
    }
    Write-Output (
        "BEACON_MANDATORY_POST_TEST_RESTORE_END " +
        "displaySwitch=$displaySwitchExit restoreBefore=$restoreBeforeExit remove=$removeExit " +
        "restoreAfter=$restoreAfterExit topology=$topologyVerified agent=$agentVerified")
}

if ($null -ne $cleanupFailure) {
    throw $cleanupFailure
}
if ($null -ne $testFailure) {
    throw $testFailure
}
