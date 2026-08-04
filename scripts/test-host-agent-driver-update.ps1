param(
    [switch]$StaticOnly,
    [switch]$TamperPackage,
    [switch]$ExpectActiveLeaseRejection,
    [string]$CandidateDirectory = "",
    [Parameter(Mandatory = $true)]
    [string]$ProtocolVersion,
    [string]$ServerBaseUrl = "https://127.0.0.1:49680"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (($StaticOnly -and ($TamperPackage -or $ExpectActiveLeaseRejection)) -or
    ($TamperPackage -and $ExpectActiveLeaseRejection)) {
    throw "Select exactly one driver-update acceptance mode."
}

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
$programDataPackage = $null
New-Item -ItemType Directory -Path $inbox -Force | Out-Null
try {
    $packageId = "sudovda-static-$([Guid]::NewGuid().ToString('N'))"
    $package = & $packageBuilderPath `
        -PackageDirectory $CandidateDirectory `
        -PackageId $packageId `
        -ProtocolVersion $ProtocolVersion `
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
    $expectedProtocol = ([Version]$ProtocolVersion).ToString()
    if ($manifest.protocolVersion -ne $expectedProtocol) {
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
    $activeDriverPath = Join-Path $env:windir "System32\drivers\UMDF\SudoVDA.dll"
    $activeHashBefore = (Get-FileHash `
        -LiteralPath $activeDriverPath `
        -Algorithm SHA256).Hash
    if ($TamperPackage) {
        [System.IO.File]::AppendAllBytes(
            (Join-Path $programDataPackage "SudoVDA.dll"),
            [byte[]](0))
    }

    $body = @{
        packageId = $packageId
        transactionId = [Guid]::NewGuid()
    }
    if ($ExpectActiveLeaseRejection) {
        $response = Invoke-WebRequest `
            -Method Post `
            -Uri "$ServerBaseUrl/admin/driver/sudovda/updates" `
            -ContentType "application/json" `
            -Body ($body | ConvertTo-Json) `
            -SkipCertificateCheck `
            -SkipHttpErrorCheck
        $activeHashAfter = (Get-FileHash `
            -LiteralPath $activeDriverPath `
            -Algorithm SHA256).Hash
        if ([int]$response.StatusCode -ne 409) {
            throw "Active-lease update returned HTTP $([int]$response.StatusCode) instead of 409."
        }
        if ($activeHashBefore -ne $activeHashAfter) {
            throw "The active SudoVDA binary changed during an active-lease rejection."
        }
        Write-Output "Host Agent active-lease rejection acceptance passed."
        Write-Output "HTTP status: $([int]$response.StatusCode)"
        return
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
    if ($TamperPackage) {
        $activeHashAfter = (Get-FileHash `
            -LiteralPath $activeDriverPath `
            -Algorithm SHA256).Hash
        if ($current.state -ne "Rejected" -or
            $current.diagnostic -ne "package-hash-mismatch") {
            throw "Tampered SudoVDA update was not rejected correctly: state=$($current.state) diagnostic=$($current.diagnostic)"
        }
        if ($activeHashBefore -ne $activeHashAfter) {
            throw "The active SudoVDA binary changed during a rejected update."
        }
        Write-Output "Host Agent rejected-package acceptance passed."
        $current | ConvertTo-Json -Depth 8
        return
    }
    if ($current.state -ne "Succeeded") {
        throw "SudoVDA update did not succeed: state=$($current.state) diagnostic=$($current.diagnostic)"
    }
    Write-Output "Host Agent driver update dynamic acceptance passed."
    $current | ConvertTo-Json -Depth 8
}
finally {
    if ($null -ne $programDataPackage -and
        (Test-Path -LiteralPath $programDataPackage)) {
        Remove-Item -LiteralPath $programDataPackage -Recurse -Force
    }
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
