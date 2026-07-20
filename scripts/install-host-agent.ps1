param(
    [string]$PublishDirectory = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path $repositoryRoot "artifacts\host-agent\publish"
}
$PublishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)

if (-not $SkipPublish) {
    & dotnet publish (Join-Path $repositoryRoot "src\Beacon.HostAgent\Beacon.HostAgent.csproj") `
        --configuration Release `
        --runtime win-x64 `
        --self-contained false `
        --output $PublishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Beacon Host Agent publish failed with exit code $LASTEXITCODE."
    }
}

$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole(
    [System.Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdministrator) {
    $escapedScript = '"' + $PSCommandPath.Replace('"', '\"') + '"'
    $escapedPublish = '"' + $PublishDirectory.Replace('"', '\"') + '"'
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File $escapedScript -PublishDirectory $escapedPublish -SkipPublish"
    $process = Start-Process `
        -FilePath "powershell.exe" `
        -Verb RunAs `
        -ArgumentList $arguments `
        -Wait `
        -PassThru
    exit $process.ExitCode
}

$executable = Join-Path $PublishDirectory "Beacon.HostAgent.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Beacon Host Agent executable was not found at $executable."
}

$ownerSid = $identity.User.Value
$ownerName = $identity.Name
$installDirectory = Join-Path $env:ProgramFiles "BeaconStream\HostAgent"
$commonApplicationData = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::CommonApplicationData)
$storageRoot = Join-Path $commonApplicationData "Beacon\HostAgent"
$taskName = "Beacon Stream Host Agent"

function Set-ProtectedDirectoryAcl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [System.Security.Principal.SecurityIdentifier]$UserSid,
        [Parameter(Mandatory = $true)]
        [System.Security.AccessControl.FileSystemRights]$UserRights,
        [Parameter(Mandatory = $true)]
        [System.Security.AccessControl.InheritanceFlags]$UserInheritance
    )

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    $administrators = [System.Security.Principal.SecurityIdentifier]::new(
        [System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid,
        $null)
    $localSystem = [System.Security.Principal.SecurityIdentifier]::new(
        [System.Security.Principal.WellKnownSidType]::LocalSystemSid,
        $null)
    $acl = [System.Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($administrators)
    $containerAndObjects = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor `
        [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
    foreach ($principalSid in @($administrators, $localSystem)) {
        $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
            $principalSid,
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            $containerAndObjects,
            [System.Security.AccessControl.PropagationFlags]::None,
            [System.Security.AccessControl.AccessControlType]::Allow))
    }
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
        $UserSid,
        $UserRights,
        $UserInheritance,
        [System.Security.AccessControl.PropagationFlags]::None,
        [System.Security.AccessControl.AccessControlType]::Allow))
    Set-Acl -LiteralPath $Path -AclObject $acl
}

$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
$runningAgentProcesses = @(Get-Process -Name "Beacon.HostAgent" -ErrorAction SilentlyContinue)
if ($null -ne $existingTask -and $existingTask.State -eq "Running") {
    Stop-ScheduledTask -TaskName $taskName
}
if ($runningAgentProcesses.Count -gt 0) {
    $runningAgentProcesses | Wait-Process -ErrorAction SilentlyContinue
}

Set-ProtectedDirectoryAcl `
    -Path $installDirectory `
    -UserSid $identity.User `
    -UserRights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute) `
    -UserInheritance (
        [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor `
        [System.Security.AccessControl.InheritanceFlags]::ObjectInherit)
Copy-Item -Path (Join-Path $PublishDirectory "*") -Destination $installDirectory -Recurse -Force

Set-ProtectedDirectoryAcl `
    -Path $storageRoot `
    -UserSid $identity.User `
    -UserRights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute) `
    -UserInheritance ([System.Security.AccessControl.InheritanceFlags]::None)
$inboxDirectory = Join-Path $storageRoot "Inbox"
Set-ProtectedDirectoryAcl `
    -Path $inboxDirectory `
    -UserSid $identity.User `
    -UserRights ([System.Security.AccessControl.FileSystemRights]::Modify) `
    -UserInheritance (
        [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor `
        [System.Security.AccessControl.InheritanceFlags]::ObjectInherit)
foreach ($name in @("Staged", "InstalledEvidence", "Transactions", "Logs")) {
    Set-ProtectedDirectoryAcl `
        -Path (Join-Path $storageRoot $name) `
        -UserSid $identity.User `
        -UserRights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute) `
        -UserInheritance (
            [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor `
            [System.Security.AccessControl.InheritanceFlags]::ObjectInherit)
}

$installedExecutable = Join-Path $installDirectory "Beacon.HostAgent.exe"
$action = New-ScheduledTaskAction `
    -Execute $installedExecutable `
    -Argument "--owner-sid $ownerSid" `
    -WorkingDirectory $installDirectory
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $ownerName
$taskPrincipal = New-ScheduledTaskPrincipal `
    -UserId $ownerName `
    -LogonType Interactive `
    -RunLevel Highest
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
    -Description "Beacon-owned elevated display and driver boundary." `
    -Force | Out-Null
Start-ScheduledTask -TaskName $taskName

Write-Output "Installed and started $taskName for $ownerName ($ownerSid)."
Write-Output "Executable: $installedExecutable"
Write-Output "Driver inbox: $inboxDirectory"
Write-Output "Diagnostics: $(Join-Path $storageRoot 'Logs\host-agent.log')"
