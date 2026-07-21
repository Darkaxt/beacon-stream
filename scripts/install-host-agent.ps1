param(
    [string]$AgentPublishDirectory = "",
    [string]$BootstrapPublishDirectory = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($AgentPublishDirectory)) {
    $AgentPublishDirectory = Join-Path $repositoryRoot "artifacts\host-agent\publish"
}
if ([string]::IsNullOrWhiteSpace($BootstrapPublishDirectory)) {
    $BootstrapPublishDirectory = Join-Path $repositoryRoot "artifacts\host-agent\bootstrap"
}
$AgentPublishDirectory = [IO.Path]::GetFullPath($AgentPublishDirectory)
$BootstrapPublishDirectory = [IO.Path]::GetFullPath($BootstrapPublishDirectory)

if (-not $SkipPublish) {
    dotnet publish (Join-Path $repositoryRoot "src\Beacon.HostAgent\Beacon.HostAgent.csproj") `
        --configuration Release `
        --runtime win-x64 `
        --self-contained false `
        --output $AgentPublishDirectory
    if ($LASTEXITCODE -ne 0) { throw "Beacon Host Agent publish failed." }

    dotnet publish `
        (Join-Path $repositoryRoot "src\Beacon.HostAgent.Bootstrap\Beacon.HostAgent.Bootstrap.csproj") `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        --output $BootstrapPublishDirectory
    if ($LASTEXITCODE -ne 0) { throw "Beacon Host Agent bootstrap publish failed." }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdministrator) {
    $escapedScript = '"' + $PSCommandPath.Replace('"', '\"') + '"'
    $escapedAgent = '"' + $AgentPublishDirectory.Replace('"', '\"') + '"'
    $escapedBootstrap = '"' + $BootstrapPublishDirectory.Replace('"', '\"') + '"'
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File $escapedScript " +
        "-AgentPublishDirectory $escapedAgent " +
        "-BootstrapPublishDirectory $escapedBootstrap -SkipPublish"
    $process = Start-Process `
        -FilePath "powershell.exe" `
        -Verb RunAs `
        -ArgumentList $arguments `
        -Wait `
        -PassThru
    exit $process.ExitCode
}

$agentExecutable = Join-Path $AgentPublishDirectory "Beacon.HostAgent.exe"
$bootstrapExecutable = Join-Path $BootstrapPublishDirectory "Beacon.HostAgent.Bootstrap.exe"
foreach ($required in @($agentExecutable, $bootstrapExecutable)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required Host Agent installation file is missing: $required."
    }
}

$ownerSid = $identity.User.Value
$ownerName = $identity.Name
$installRoot = Join-Path $env:ProgramFiles "BeaconStream"
$bootstrapRoot = Join-Path $installRoot "Bootstrap"
$versionsRoot = Join-Path $installRoot "HostAgent\Versions"
$agentHash = (Get-FileHash -LiteralPath $agentExecutable -Algorithm SHA256).Hash
$versionId = "agent-bootstrap-$($agentHash.Substring(0, 16).ToLowerInvariant())"
$versionRoot = Join-Path $versionsRoot $versionId
$commonApplicationData = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::CommonApplicationData)
$storageRoot = Join-Path $commonApplicationData "Beacon\HostAgent"
$taskName = "Beacon Stream Host Agent"

function Set-ProtectedDirectoryAcl {
    param(
        [Parameter(Mandatory = $true)] [string]$Path,
        [Parameter(Mandatory = $true)]
        [Security.Principal.SecurityIdentifier]$UserSid,
        [Parameter(Mandatory = $true)]
        [Security.AccessControl.FileSystemRights]$UserRights,
        [Parameter(Mandatory = $true)]
        [Security.AccessControl.InheritanceFlags]$UserInheritance
    )

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    $administrators = [Security.Principal.SecurityIdentifier]::new(
        [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
    $localSystem = [Security.Principal.SecurityIdentifier]::new(
        [Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($administrators)
    $containerAndObjects = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor `
        [Security.AccessControl.InheritanceFlags]::ObjectInherit
    foreach ($principalSid in @($administrators, $localSystem)) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $principalSid,
            [Security.AccessControl.FileSystemRights]::FullControl,
            $containerAndObjects,
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow))
    }
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $UserSid,
        $UserRights,
        $UserInheritance,
        [Security.AccessControl.PropagationFlags]::None,
        [Security.AccessControl.AccessControlType]::Allow))
    Set-Acl -LiteralPath $Path -AclObject $acl
}

$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($null -ne $existingTask -and $existingTask.State -eq "Running") {
    Stop-ScheduledTask -TaskName $taskName
}
$running = @(
    Get-Process -Name "Beacon.HostAgent", "Beacon.HostAgent.Bootstrap" `
        -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    $running | Wait-Process -ErrorAction SilentlyContinue
}

$readExecute = [Security.AccessControl.FileSystemRights]::ReadAndExecute
$inheritAll = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor `
    [Security.AccessControl.InheritanceFlags]::ObjectInherit
Set-ProtectedDirectoryAcl $bootstrapRoot $identity.User $readExecute $inheritAll
Set-ProtectedDirectoryAcl $versionsRoot $identity.User $readExecute $inheritAll
Set-ProtectedDirectoryAcl $versionRoot $identity.User $readExecute $inheritAll
Copy-Item -LiteralPath $bootstrapExecutable `
    -Destination (Join-Path $bootstrapRoot "Beacon.HostAgent.Bootstrap.exe") -Force
Copy-Item -Path (Join-Path $AgentPublishDirectory "*") `
    -Destination $versionRoot -Recurse -Force

Set-ProtectedDirectoryAcl `
    $storageRoot $identity.User $readExecute `
    ([Security.AccessControl.InheritanceFlags]::None)
$inboxRoot = Join-Path $storageRoot "Inbox"
Set-ProtectedDirectoryAcl `
    $inboxRoot $identity.User `
    ([Security.AccessControl.FileSystemRights]::Modify) $inheritAll
foreach ($relative in @(
    "Staged", "InstalledEvidence", "Transactions", "Logs",
    "StagedHostAgent", "HostAgentTransactions", "State")) {
    Set-ProtectedDirectoryAcl `
        (Join-Path $storageRoot $relative) $identity.User $readExecute $inheritAll
}

$currentVersionPath = Join-Path $storageRoot "State\current-version.json"
$currentVersion = [ordered]@{
    versionId = $versionId
    sourceCommit = $agentHash
} | ConvertTo-Json
$temporaryState = "$currentVersionPath.$([Guid]::NewGuid().ToString('N')).tmp"
[IO.File]::WriteAllText($temporaryState, $currentVersion)
Move-Item -LiteralPath $temporaryState -Destination $currentVersionPath -Force

$installedBootstrap = Join-Path $bootstrapRoot "Beacon.HostAgent.Bootstrap.exe"
$action = New-ScheduledTaskAction `
    -Execute $installedBootstrap `
    -Argument "--owner-sid $ownerSid" `
    -WorkingDirectory $bootstrapRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $ownerName
$taskPrincipal = New-ScheduledTaskPrincipal `
    -UserId $ownerName -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew
Register-ScheduledTask `
    -TaskName $taskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $taskPrincipal `
    -Settings $settings `
    -Description "Beacon stable elevated Host Agent bootstrap." `
    -Force | Out-Null
Start-ScheduledTask -TaskName $taskName

Write-Output "Installed and started $taskName for $ownerName ($ownerSid)."
Write-Output "Bootstrap: $installedBootstrap"
Write-Output "Initial version: $versionId ($agentHash)"
Write-Output "Host Agent inbox: $inboxRoot"
Write-Output "Diagnostics: $(Join-Path $storageRoot 'Logs')"
