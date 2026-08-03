[CmdletBinding()]
param(
    [string]$Serial = 'emulator-5554',
    [string]$EvidenceDirectory,
    [string]$BeforeConnectSignalPath,
    [switch]$ArtifactsReady
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Gate5CleanupCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    try {
        $output = (& dotnet @Arguments 2>&1 | Out-String).Trim()
        return [pscustomobject]@{
            Name = $Name
            ExitCode = $LASTEXITCODE
            Output = $output
            Error = $null
        }
    } catch {
        return [pscustomobject]@{
            Name = $Name
            ExitCode = -1
            Output = ''
            Error = $_.Exception.Message
        }
    }
}

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
    try { Write-Output 'BEACON_MANDATORY_POST_TEST_RESTORE_BEGIN' } catch {}
    $displayProbeArguments = @(
        'run', '--project', (Join-Path $repositoryRoot 'src\Beacon.DisplayProbe'),
        '--configuration', 'Release', '--no-build', '--')
    $hostAgentArguments = @(
        'run', '--project', (Join-Path $repositoryRoot 'src\Beacon.HostAgent.Control'),
        '--configuration', 'Release', '--no-build', '--')

    try {
        $displaySwitchProcess = Start-Process `
            -FilePath (Join-Path $env:WINDIR 'System32\DisplaySwitch.exe') `
            -ArgumentList '/internal' `
            -PassThru `
            -Wait `
            -WindowStyle Hidden
        $displaySwitch = [pscustomobject]@{
            Name = 'force-physical'
            ExitCode = $displaySwitchProcess.ExitCode
            Output = ''
            Error = $null
        }
        $displaySwitchProcess.Dispose()
    } catch {
        $displaySwitch = [pscustomobject]@{
            Name = 'force-physical'
            ExitCode = -1
            Output = ''
            Error = $_.Exception.Message
        }
    }
    $restoreBeforeGuard = Invoke-Gate5CleanupCommand `
        -Name 'restore-before-guard' `
        -Arguments ($displayProbeArguments + @('restore-physical'))

    $guardFailure = $null
    try {
        $guardPidPath = Join-Path $effectiveEvidenceDirectory 'display-guard.pid'
        if (Test-Path -LiteralPath $guardPidPath) {
            $guardPidValue = (Get-Content -LiteralPath $guardPidPath -Raw).Trim()
            $guardProcessId = 0
            if ([int]::TryParse($guardPidValue, [ref]$guardProcessId)) {
                $guardProcess = Get-Process -Id $guardProcessId -ErrorAction SilentlyContinue
                if ($null -ne $guardProcess) {
                    $guardProcess.WaitForExit()
                    $guardProcess.Dispose()
                }
            }
        }
    } catch {
        $guardFailure = $_.Exception.Message
    }
    $cleanupCommands = @(
        $displaySwitch,
        $restoreBeforeGuard,
        (Invoke-Gate5CleanupCommand `
            -Name 'remove' `
            -Arguments ($displayProbeArguments + @('remove', '--client', $clientId))),
        (Invoke-Gate5CleanupCommand `
            -Name 'restore-after-remove' `
            -Arguments ($displayProbeArguments + @('restore-physical'))),
        (Invoke-Gate5CleanupCommand `
            -Name 'display-status' `
            -Arguments ($displayProbeArguments + @('status'))),
        (Invoke-Gate5CleanupCommand `
            -Name 'host-agent-status' `
            -Arguments ($hostAgentArguments + @('status')))
    )
    foreach ($command in $cleanupCommands) {
        try {
            Write-Output "BEACON_GATE5_CLEANUP_COMMAND name=$($command.Name) exit=$($command.ExitCode)"
            if (-not [string]::IsNullOrWhiteSpace($command.Output)) {
                Write-Output $command.Output
            }
            if (-not [string]::IsNullOrWhiteSpace($command.Error)) {
                Write-Warning $command.Error
            }
        } catch {}
    }

    $byName = @{}
    foreach ($command in $cleanupCommands) {
        $byName[$command.Name] = $command
    }
    $statusOutput = $byName['display-status'].Output
    $topologyVerified =
        $byName['display-status'].ExitCode -eq 0 -and
        $statusOutput.Contains('mirrorMode=False', [StringComparison]::Ordinal) -and
        $statusOutput.Contains('physicalPrimaryVerified=True', [StringComparison]::Ordinal) -and
        $statusOutput.Contains('kind=Physical', [StringComparison]::Ordinal) -and
        $statusOutput.Contains('primary=True', [StringComparison]::Ordinal) -and
        -not $statusOutput.Contains('kind=Virtual', [StringComparison]::Ordinal)
    $agentVerified = $false
    if ($byName['host-agent-status'].ExitCode -eq 0) {
        try {
            $agentStatus = $byName['host-agent-status'].Output | ConvertFrom-Json -Depth 16
            $agentVerified =
                $agentStatus.success -eq $true -and
                $agentStatus.payload.lease.leaseCount -eq 0 -and
                $agentStatus.payload.lease.heartbeatActive -eq $false
        } catch {
            $agentVerified = $false
        }
    }
    $removeOutput = $byName['remove'].Output
    $removeVerified =
        $byName['remove'].ExitCode -eq 0 -or
        ($byName['remove'].ExitCode -eq 2 -and
            $removeOutput.StartsWith(
                'remove: failed: No active SudoVDA driver lease owns client-',
                [StringComparison]::Ordinal))
    $commandsVerified =
        $byName['force-physical'].ExitCode -eq 0 -and
        $byName['restore-before-guard'].ExitCode -eq 0 -and
        $removeVerified -and
        $byName['restore-after-remove'].ExitCode -eq 0
    $cleanupVerified = $commandsVerified -and $topologyVerified -and $agentVerified

    $evidenceRetained = $false
    $evidenceFailure = $null
    try {
        [pscustomobject]@{
            Verified = $cleanupVerified
            CommandsVerified = $commandsVerified
            TopologyVerified = $topologyVerified
            AgentVerified = $agentVerified
            GuardFailure = $guardFailure
            TestFailure = if ($null -eq $testFailure) { $null } else { $testFailure.ToString() }
            Commands = $cleanupCommands
        } | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (
            Join-Path $effectiveEvidenceDirectory 'mandatory-post-test-recovery.json') -Encoding utf8
        $evidenceRetained = $true
    } catch {
        $evidenceFailure = $_.Exception.Message
        try { Write-Warning "Gate 5 cleanup evidence could not be retained: $evidenceFailure" } catch {}
    }

    $cleanupSummary =
        "commands=$commandsVerified topology=$topologyVerified agent=$agentVerified " +
        "evidence=$evidenceRetained guardError=$($null -ne $guardFailure)"
    if (-not $cleanupVerified -or -not $evidenceRetained) {
        $cleanupFailure = "Gate 5 mandatory cleanup was not verified. $cleanupSummary " +
            "evidenceError=$evidenceFailure"
    }
    try { Write-Output "BEACON_MANDATORY_POST_TEST_RESTORE_END $cleanupSummary" } catch {}
}

if ($null -ne $cleanupFailure) {
    if ($null -ne $testFailure) {
        throw [AggregateException]::new(
            'Gate 5 execution and mandatory cleanup both failed.',
            @($testFailure.Exception, [InvalidOperationException]::new($cleanupFailure)))
    }
    throw $cleanupFailure
}
if ($null -ne $testFailure) {
    throw $testFailure
}
