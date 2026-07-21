param(
    [string]$Repository = "Darkaxt/beacon-stream",
    [string]$Branch = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not [string]::IsNullOrWhiteSpace((git -C $repositoryRoot status --porcelain))) {
    throw "The Host Agent update source must be a clean committed revision."
}
if ([string]::IsNullOrWhiteSpace($Branch)) {
    $Branch = (git -C $repositoryRoot branch --show-current).Trim()
}
if ([string]::IsNullOrWhiteSpace($Branch)) {
    throw "A pushed branch is required for the Host Agent update workflow."
}

$sourceCommit = (git -C $repositoryRoot rev-parse HEAD).Trim()
git -C $repositoryRoot fetch origin $Branch | Out-Null
$remoteCommit = (git -C $repositoryRoot rev-parse "origin/$Branch").Trim()
if ($sourceCommit -ne $remoteCommit) {
    throw "The current Host Agent update revision is not synchronized to origin/$Branch."
}

$requestId = [Guid]::NewGuid().ToString("N")
$transactionId = [Guid]::NewGuid()
$runTitle = "Host Agent Update $requestId"
Write-Output "Dispatching signed Host Agent package request=$requestId source=$sourceCommit."
gh workflow run host-agent-update.yml `
    --repo $Repository `
    --ref $Branch `
    -f "request_id=$requestId"
if ($LASTEXITCODE -ne 0) {
    throw "Host Agent update workflow dispatch failed with exit code $LASTEXITCODE."
}

$run = $null
while ($null -eq $run) {
    $runs = gh run list `
        --repo $Repository `
        --workflow host-agent-update.yml `
        --branch $Branch `
        --event workflow_dispatch `
        --limit 30 `
        --json databaseId,displayTitle,headSha | ConvertFrom-Json
    $run = $runs | Where-Object {
        $_.displayTitle -eq $runTitle -and $_.headSha -eq $sourceCommit
    } | Select-Object -First 1
    if ($null -eq $run) {
        Write-Output "Waiting for GitHub to register request=$requestId."
        Start-Sleep -Seconds 2
    }
}

Write-Output "Watching workflow run=$($run.databaseId)."
gh run watch $run.databaseId --repo $Repository --exit-status
if ($LASTEXITCODE -ne 0) {
    throw "Host Agent update workflow failed with exit code $LASTEXITCODE."
}

$downloadRoot = Join-Path $repositoryRoot "artifacts\host-agent\downloads\$requestId"
New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
$artifactName = "beacon-host-agent-$requestId-$sourceCommit"
gh run download $run.databaseId `
    --repo $Repository `
    --name $artifactName `
    --dir $downloadRoot
if ($LASTEXITCODE -ne 0) {
    throw "Host Agent update artifact download failed with exit code $LASTEXITCODE."
}

$packageProject = Join-Path $repositoryRoot "src\Beacon.HostAgent.Package\Beacon.HostAgent.Package.csproj"
$controlProject = Join-Path $repositoryRoot "src\Beacon.HostAgent.Control\Beacon.HostAgent.Control.csproj"
dotnet build $packageProject --configuration Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Host Agent package tool build failed." }
dotnet build $controlProject --configuration Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Host Agent control build failed." }
dotnet run --project $packageProject --configuration Release --no-build -- `
    verify --package-root $downloadRoot --bootstrap-version "1.0.0" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Downloaded Host Agent package verification failed." }

$manifest = Get-Content -LiteralPath (Join-Path $downloadRoot "manifest.json") `
    -Raw | ConvertFrom-Json
$packageId = [string]$manifest.packageId
$commonApplicationData = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::CommonApplicationData)
$hostAgentData = Join-Path $commonApplicationData "Beacon\HostAgent"
$inboxPackage = Join-Path $hostAgentData "Inbox\$packageId"
if (Test-Path -LiteralPath $inboxPackage) {
    throw "The unique Host Agent inbox package already exists: $packageId."
}
New-Item -ItemType Directory -Path $inboxPackage | Out-Null
Copy-Item -LiteralPath (Join-Path $downloadRoot "manifest.json") -Destination $inboxPackage
Copy-Item -LiteralPath (Join-Path $downloadRoot "manifest.sig") -Destination $inboxPackage
Copy-Item -LiteralPath (Join-Path $downloadRoot "payload") `
    -Destination $inboxPackage -Recurse

$installOutput = @(dotnet run --project $controlProject --configuration Release --no-build -- `
    install --package-id $packageId --transaction-id $transactionId.ToString("D"))
if ($LASTEXITCODE -ne 0) {
    throw "Host Agent rejected update transaction $transactionId."
}
$installResponse = $installOutput[-1] | ConvertFrom-Json
if (-not $installResponse.success -or $installResponse.payload.state -ne "staged") {
    throw "Host Agent did not return a staged update transaction."
}

$finalResponse = $null
while ($null -eq $finalResponse) {
    $queryOutput = @(dotnet run --project $controlProject --configuration Release --no-build -- `
        query --transaction-id $transactionId.ToString("D") 2>$null)
    if ($LASTEXITCODE -eq 0 -and $queryOutput.Count -gt 0) {
        $candidate = $queryOutput[-1] | ConvertFrom-Json
        if ($candidate.success -and $candidate.payload.state -in @(
            "succeeded", "rolledBack", "degraded")) {
            $finalResponse = $candidate
            continue
        }
    }

    $bootstrap = Get-CimInstance Win32_Process -Filter `
        "Name = 'Beacon.HostAgent.Bootstrap.exe'" -ErrorAction SilentlyContinue
    if ($null -eq $bootstrap) {
        $journalPath = Join-Path $hostAgentData `
            "HostAgentTransactions\$($transactionId.ToString('D')).json"
        if (Test-Path -LiteralPath $journalPath) {
            $journal = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
            throw "Host Agent bootstrap stopped in durable state '$($journal.state)'."
        }
        throw "Host Agent bootstrap stopped before transaction state was available."
    }
    Write-Output "Waiting for Host Agent transaction=$transactionId to reach a terminal state."
    Start-Sleep -Seconds 2
}

if ($finalResponse.payload.state -ne "succeeded") {
    throw "Host Agent update ended in state '$($finalResponse.payload.state)'."
}

$task = Get-ScheduledTask -TaskName "Beacon Stream Host Agent"
$taskExecutable = [IO.Path]::GetFullPath([string]$task.Actions.Execute)
if ([IO.Path]::GetFileName($taskExecutable) -ne "Beacon.HostAgent.Bootstrap.exe") {
    throw "The Host Agent scheduled task does not target the stable bootstrap."
}
$installRoot = Split-Path -Parent (Split-Path -Parent $taskExecutable)
$installedRoot = Join-Path $installRoot "HostAgent\Versions\$packageId"
foreach ($file in $manifest.files) {
    $installedPath = Join-Path $installedRoot ([string]$file.path).Replace('/', '\')
    if (-not (Test-Path -LiteralPath $installedPath -PathType Leaf)) {
        throw "Installed Host Agent package file is missing: $($file.path)."
    }
    $hash = (Get-FileHash -LiteralPath $installedPath -Algorithm SHA256).Hash
    if ($hash -ne [string]$file.sha256) {
        throw "Installed Host Agent package hash mismatch: $($file.path)."
    }
}

$bootstrapProcess = Get-CimInstance Win32_Process -Filter `
    "Name = 'Beacon.HostAgent.Bootstrap.exe'" | Select-Object -First 1
$agentProcess = Get-CimInstance Win32_Process -Filter `
    "Name = 'Beacon.HostAgent.exe'" | Select-Object -First 1
if ($null -eq $bootstrapProcess -or $null -eq $agentProcess `
    -or $agentProcess.ParentProcessId -ne $bootstrapProcess.ProcessId) {
    throw "Installed Host Agent is not supervised by the stable bootstrap."
}

Write-Output "Host Agent update succeeded."
Write-Output "Workflow run: $($run.databaseId)"
Write-Output "Transaction: $transactionId"
Write-Output "Package: $packageId"
Write-Output "Source: $sourceCommit"
