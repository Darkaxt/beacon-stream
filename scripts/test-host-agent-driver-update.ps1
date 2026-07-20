param(
    [switch]$StaticOnly,
    [string]$CandidateDirectory = "",
    [string]$ServerBaseUrl = "https://127.0.0.1:49680"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$installerPath = Join-Path $PSScriptRoot "install-host-agent.ps1"
$packageBuilderPath = Join-Path $PSScriptRoot "new-sudovda-package.ps1"
foreach ($path in @($installerPath, $packageBuilderPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required driver update script is missing: $path"
    }
    $tokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $path,
        [ref]$tokens,
        [ref]$parseErrors)
    if ($parseErrors.Count -ne 0) {
        throw "PowerShell parser rejected ${path}: $($parseErrors[0].Message)"
    }
}

$installer = Get-Content -LiteralPath $installerPath -Raw
foreach ($requiredText in @(
    "CommonApplicationData",
    "Inbox",
    "Staged",
    "InstalledEvidence",
    "Transactions",
    "Logs",
    "SetAccessRuleProtection"
)) {
    if (-not $installer.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "Host Agent installer does not declare required storage/ACL behavior: $requiredText"
    }
}

if ([string]::IsNullOrWhiteSpace($CandidateDirectory)) {
    $CandidateDirectory = Join-Path `
        (Split-Path -Parent $repositoryRoot) `
        "SudoVDA-watchdog\Virtual Display Driver (HDR)\x64\Debug\SudoVDA"
}
$CandidateDirectory = [System.IO.Path]::GetFullPath($CandidateDirectory)
if (-not (Test-Path -LiteralPath $CandidateDirectory -PathType Container)) {
    throw "SudoVDA candidate directory was not found: $CandidateDirectory"
}

$scratch = Join-Path $repositoryRoot "artifacts\driver-update-acceptance\$([Guid]::NewGuid().ToString('N'))"
$inbox = Join-Path $scratch "Inbox"
New-Item -ItemType Directory -Path $inbox -Force | Out-Null
try {
    $packageId = "sudovda-static-$([Guid]::NewGuid().ToString('N'))"
    $package = & $packageBuilderPath `
        -PackageDirectory $CandidateDirectory `
        -PackageId $packageId `
        -ProtocolVersion "0.2.0" `
        -InboxRoot $inbox

    $packageRoot = Join-Path $inbox $packageId
    $manifestPath = Join-Path $packageRoot "manifest.json"
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.architecture -ne "x64") {
        throw "Generated SudoVDA manifest schema or architecture is invalid."
    }
    if ($manifest.hardwareId -ne "ROOT\SudoMaker\SudoVDA") {
        throw "Generated SudoVDA manifest hardware id is invalid."
    }
    if ($manifest.protocolVersion -ne "0.2.0") {
        throw "Generated SudoVDA manifest protocol is invalid."
    }
    $actualFiles = @(
        Get-ChildItem -LiteralPath $packageRoot -File |
            ForEach-Object Name |
            Sort-Object
    )
    $declaredFiles = @($manifest.files | ForEach-Object path)
    $expectedFiles = @("manifest.json") + $declaredFiles
    if (Compare-Object ($actualFiles | Sort-Object) ($expectedFiles | Sort-Object)) {
        throw "Generated SudoVDA package contains an unexpected file set."
    }
    foreach ($file in $manifest.files) {
        $actualHash = (Get-FileHash `
            -LiteralPath (Join-Path $packageRoot $file.path) `
            -Algorithm SHA256).Hash
        if ($actualHash -ne $file.sha256) {
            throw "Generated SudoVDA package hash mismatch: $($file.path)"
        }
    }

    if ($StaticOnly) {
        Write-Output "Host Agent driver update static acceptance passed."
        Write-Output "Package: $packageRoot"
        return
    }

    $programDataInbox = Join-Path `
        ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) `
        "Beacon\HostAgent\Inbox"
    $programDataPackage = Join-Path $programDataInbox $packageId
    if (Test-Path -LiteralPath $programDataPackage) {
        throw "ProgramData package id already exists: $packageId"
    }
    Move-Item -LiteralPath $packageRoot -Destination $programDataPackage

    $body = @{
        packageId = $packageId
        transactionId = [Guid]::NewGuid()
    }
    $accepted = Invoke-RestMethod `
        -Method Post `
        -Uri "$ServerBaseUrl/admin/driver/sudovda/updates" `
        -ContentType "application/json" `
        -Body ($body | ConvertTo-Json) `
        -SkipCertificateCheck
    $terminalStates = @("Succeeded", "Rejected", "RolledBack", "Degraded")
    $current = $accepted
    while ($terminalStates -notcontains $current.state) {
        Start-Sleep -Seconds 1
        $current = Invoke-RestMethod `
            -Method Get `
            -Uri "$ServerBaseUrl/admin/driver/sudovda/updates/$($body.transactionId)" `
            -SkipCertificateCheck
    }
    if ($current.state -ne "Succeeded") {
        throw "SudoVDA update did not succeed: state=$($current.state) diagnostic=$($current.diagnostic)"
    }
    Write-Output "Host Agent driver update dynamic acceptance passed."
    $current | ConvertTo-Json -Depth 8
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
