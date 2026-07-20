param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$PackageId,

    [Parameter(Mandatory = $true)]
    [string]$ProtocolVersion,

    [string]$InboxRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Remove-InfComment {
    param([string]$Value)

    $quoted = $false
    for ($index = 0; $index -lt $Value.Length; $index++) {
        if ($Value[$index] -eq '"') {
            $quoted = -not $quoted
        }
        elseif ($Value[$index] -eq ';' -and -not $quoted) {
            return $Value.Substring(0, $index)
        }
    }
    return $Value
}

function Read-InfDocument {
    param([string]$Path)

    $sections = @{}
    $current = $null
    foreach ($rawLine in Get-Content -LiteralPath $Path) {
        $line = $rawLine.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith(';')) {
            continue
        }
        if ($line.StartsWith('[') -and $line.EndsWith(']')) {
            $sectionName = $line.Substring(1, $line.Length - 2).Trim()
            if (-not $sections.ContainsKey($sectionName)) {
                $sections[$sectionName] = [System.Collections.Generic.List[object]]::new()
            }
            $current = $sections[$sectionName]
            continue
        }
        if ($null -eq $current) {
            throw "INF entry appears before a section."
        }
        $equals = $line.IndexOf('=')
        if ($equals -le 0) {
            continue
        }
        $current.Add([pscustomobject]@{
            Key = $line.Substring(0, $equals).Trim()
            Value = (Remove-InfComment $line.Substring($equals + 1)).Trim()
        })
    }
    return $sections
}

function Get-InfValue {
    param(
        [hashtable]$Document,
        [string]$Section,
        [string]$Key
    )

    if (-not $Document.ContainsKey($Section)) {
        throw "INF section is missing: $Section"
    }
    $entry = $Document[$Section] |
        Where-Object { $_.Key -eq $Key } |
        Select-Object -First 1
    if ($null -eq $entry) {
        throw "INF value is missing: $Section/$Key"
    }
    return $entry.Value.Trim().Trim('"')
}

if ($PackageId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$') {
    throw "SudoVDA package id is invalid."
}
$parsedProtocol = [Version]::new()
if (-not [Version]::TryParse($ProtocolVersion, [ref]$parsedProtocol) -or
    $parsedProtocol -lt [Version]::new(0, 2, 0)) {
    throw "SudoVDA protocol version must be at least 0.2.0."
}

$PackageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)
if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
    throw "SudoVDA package directory was not found: $PackageDirectory"
}
if ([string]::IsNullOrWhiteSpace($InboxRoot)) {
    $InboxRoot = Join-Path `
        ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) `
        "Beacon\HostAgent\Inbox"
}
$InboxRoot = [System.IO.Path]::GetFullPath($InboxRoot)
New-Item -ItemType Directory -Path $InboxRoot -Force | Out-Null

$sourceEntries = @(Get-ChildItem -LiteralPath $PackageDirectory -Force)
if ($sourceEntries | Where-Object {
    ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or $_.PSIsContainer
}) {
    throw "SudoVDA package directory must contain only regular files."
}
$infFiles = @($sourceEntries | Where-Object { $_.Extension -eq '.inf' })
if ($infFiles.Count -ne 1) {
    throw "SudoVDA package directory must contain exactly one INF."
}

$inf = $infFiles[0]
$document = Read-InfDocument $inf.FullName
if ((Get-InfValue $document "Version" "Class") -ne "Display") {
    throw "SudoVDA INF class must be Display."
}
$catalogName = Get-InfValue $document "Version" "CatalogFile"
$driverVersionParts = (Get-InfValue $document "Version" "DriverVer").Split(',', 2)
$parsedDriverVersion = [Version]::new()
if ($driverVersionParts.Count -ne 2 -or
    -not [Version]::TryParse($driverVersionParts[1].Trim(), [ref]$parsedDriverVersion)) {
    throw "SudoVDA INF DriverVer is invalid."
}
$driverVersion = $driverVersionParts[1].Trim()
if (-not $document.ContainsKey("Manufacturer") -or
    -not ($document["Manufacturer"].Value -match '(^|,)\s*NTamd64\s*($|,)')) {
    throw "SudoVDA INF does not declare NTamd64."
}
if (-not $document.ContainsKey("Standard.NTamd64") -or
    -not ($document["Standard.NTamd64"].Value -match 'Root\\SudoMaker\\SudoVDA')) {
    throw "SudoVDA INF hardware id is invalid."
}
if (-not $document.ContainsKey("SourceDisksFiles")) {
    throw "SudoVDA INF SourceDisksFiles section is missing."
}
$binaryNames = @($document["SourceDisksFiles"] | ForEach-Object { $_.Key.Trim().Trim('"') })
$expectedNames = @($inf.Name, $catalogName) + $binaryNames
$actualNames = @($sourceEntries | ForEach-Object Name)
if (Compare-Object ($expectedNames | Sort-Object) ($actualNames | Sort-Object)) {
    throw "SudoVDA source directory does not exactly match the INF package file set."
}

$signedNames = @($catalogName) + $binaryNames
$signatures = @()
foreach ($name in $signedNames) {
    $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $PackageDirectory $name)
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate) {
        throw "SudoVDA package signature is invalid: $name ($($signature.Status))"
    }
    $signatures += $signature
}
$signerSubject = $signatures[0].SignerCertificate.Subject
$signerThumbprint = $signatures[0].SignerCertificate.Thumbprint.ToUpperInvariant()
foreach ($signature in $signatures) {
    if ($signature.SignerCertificate.Subject -ne $signerSubject -or
        $signature.SignerCertificate.Thumbprint.ToUpperInvariant() -ne $signerThumbprint) {
        throw "SudoVDA package files are not signed by one certificate."
    }
}

$finalRoot = Join-Path $InboxRoot $PackageId
$temporaryRoot = Join-Path $InboxRoot ".staging-$([Guid]::NewGuid().ToString('N'))"
if (Test-Path -LiteralPath $finalRoot) {
    throw "SudoVDA package id already exists in the inbox: $PackageId"
}
try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    foreach ($name in $expectedNames) {
        Copy-Item `
            -LiteralPath (Join-Path $PackageDirectory $name) `
            -Destination (Join-Path $temporaryRoot $name)
    }
    $manifestFiles = @(
        foreach ($name in $expectedNames) {
            [ordered]@{
                path = $name
                sha256 = (Get-FileHash `
                    -LiteralPath (Join-Path $temporaryRoot $name) `
                    -Algorithm SHA256).Hash
            }
        }
    )
    $manifest = [ordered]@{
        schemaVersion = 1
        packageVersion = $driverVersion
        architecture = "x64"
        hardwareId = "ROOT\SudoMaker\SudoVDA"
        protocolVersion = $parsedProtocol.ToString()
        signerSubject = $signerSubject
        signerThumbprint = $signerThumbprint
        infPath = $inf.Name
        files = $manifestFiles
    }
    $manifest | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath (Join-Path $temporaryRoot "manifest.json") -Encoding utf8NoBOM
    Move-Item -LiteralPath $temporaryRoot -Destination $finalRoot
    [pscustomobject]@{
        PackageId = $PackageId
        PackageRoot = $finalRoot
        PackageVersion = $driverVersion
        ProtocolVersion = $parsedProtocol.ToString()
        SignerSubject = $signerSubject
        SignerThumbprint = $signerThumbprint
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
