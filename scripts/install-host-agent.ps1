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
$installDirectory = Join-Path $env:LOCALAPPDATA "BeaconStream\HostAgent"
$taskName = "Beacon Stream Host Agent"

$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($null -ne $existingTask -and $existingTask.State -eq "Running") {
    Stop-ScheduledTask -TaskName $taskName
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $PublishDirectory "*") -Destination $installDirectory -Recurse -Force

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
Write-Output "Diagnostics: $(Join-Path $env:LOCALAPPDATA 'BeaconStream\host-agent.log')"
