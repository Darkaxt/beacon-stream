param(
    [string]$EvidenceDirectory,
    [string]$CanaryManifest,
    [switch]$ValidateFixtures
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:Gate3EvidenceFiles = @(
    'server-output.log',
    'worker-diagnostics.json',
    'diagnostic-journal.json',
    'instrumentation.log',
    'logcat.log'
)
$script:Gate3CanaryCategories = [ordered]@{
    stream_ticket = 'stream-ticket'
    client_credential = 'client-credential'
    private_key = 'private-key'
    worker_executable_path = 'worker-executable-path'
    input_payload = 'input-payload'
}

function Read-Gate3CanaryManifest([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or
        -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw 'Gate 3 canary manifest is unavailable.'
    }

    $manifest = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $values = [ordered]@{}
    $allValues = [Collections.Generic.List[string]]::new()
    foreach ($property in $script:Gate3CanaryCategories.Keys) {
        $propertyValues = @(
            $manifest.$property |
                ForEach-Object { [string]$_ } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($propertyValues.Count -eq 0) {
            throw "Gate 3 canary category '$($script:Gate3CanaryCategories[$property])' is unavailable."
        }
        $values[$property] = @($propertyValues)
        foreach ($value in $propertyValues) {
            $allValues.Add($value)
        }
    }

    $distinct = @($allValues | Sort-Object -Unique)
    if ($distinct.Count -ne $allValues.Count) {
        throw 'Gate 3 canary values must be unique.'
    }
    return $values
}

function Invoke-Gate3SecretAbsenceCheck(
    [string]$EvidenceDirectory,
    [string]$CanaryManifest) {
    if ([string]::IsNullOrWhiteSpace($EvidenceDirectory) -or
        -not (Test-Path -LiteralPath $EvidenceDirectory -PathType Container)) {
        throw 'Gate 3 evidence directory is unavailable.'
    }

    $canaries = Read-Gate3CanaryManifest $CanaryManifest
    foreach ($evidenceFile in $script:Gate3EvidenceFiles) {
        $path = Join-Path $EvidenceDirectory $evidenceFile
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Gate 3 evidence channel '$evidenceFile' is unavailable."
        }

        $contents = Get-Content -LiteralPath $path -Raw
        foreach ($property in $script:Gate3CanaryCategories.Keys) {
            foreach ($canary in @($canaries[$property])) {
                $exposed = $contents.Contains($canary, [StringComparison]::Ordinal)
                if (-not $exposed -and $property -eq 'private_key') {
                    $exposed = ([Regex]::Replace($contents, '\s', '')).Contains(
                        [Regex]::Replace($canary, '\s', ''),
                        [StringComparison]::Ordinal)
                }
                if ($exposed) {
                    throw (
                        'Gate 3 secret exposure detected: {0} ({1}).' -f
                        $script:Gate3CanaryCategories[$property],
                        [IO.Path]::GetFileNameWithoutExtension($evidenceFile))
                }
            }
        }
    }

    Write-Output 'BEACON_GATE3_SECRET_ABSENCE_OK'
}

function Assert-Gate3SecretAbsenceFixtures() {
    $fixtureRoot = Join-Path (
        [IO.Path]::GetTempPath()) "beacon-gate3-secret-fixture-$([Guid]::NewGuid().ToString('N'))"
    $manifestPath = Join-Path $fixtureRoot 'canaries.json'
    $values = [ordered]@{
        stream_ticket = "ticket-$([Guid]::NewGuid().ToString('N'))"
        client_credential = "credential-$([Guid]::NewGuid().ToString('N'))"
        private_key = @(
            "private-key-$([Guid]::NewGuid().ToString('N'))",
            "private-key-export-$([Guid]::NewGuid().ToString('N'))")
        worker_executable_path = "worker-path-$([Guid]::NewGuid().ToString('N'))"
        input_payload = "input-$([Guid]::NewGuid().ToString('N'))"
    }

    try {
        New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
        $values | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
        foreach ($evidenceFile in $script:Gate3EvidenceFiles) {
            Set-Content -LiteralPath (Join-Path $fixtureRoot $evidenceFile) `
                -Value 'sanitized evidence' -Encoding utf8NoBOM
        }

        [void](Invoke-Gate3SecretAbsenceCheck $fixtureRoot $manifestPath)
        foreach ($property in $script:Gate3CanaryCategories.Keys) {
            foreach ($canary in @($values[$property])) {
                foreach ($evidenceFile in $script:Gate3EvidenceFiles) {
                    $path = Join-Path $fixtureRoot $evidenceFile
                    Set-Content -LiteralPath $path -Value $canary -Encoding utf8NoBOM
                    $detected = $false
                    try {
                        [void](Invoke-Gate3SecretAbsenceCheck $fixtureRoot $manifestPath)
                    }
                    catch {
                        $detected = $true
                        $message = $_.Exception.Message
                        if ($message.Contains($canary, [StringComparison]::Ordinal)) {
                            throw 'Secret fixture failure disclosed its canary value.'
                        }
                        if (-not $message.Contains(
                            $script:Gate3CanaryCategories[$property],
                            [StringComparison]::Ordinal)) {
                            throw "Secret fixture reported the wrong category for '$evidenceFile'."
                        }
                    }
                    finally {
                        Set-Content -LiteralPath $path -Value 'sanitized evidence' -Encoding utf8NoBOM
                    }
                    if (-not $detected) {
                        throw "Secret fixture did not detect category '$($script:Gate3CanaryCategories[$property])'."
                    }
                }
            }
        }
    }
    finally {
        $resolvedRoot = [IO.Path]::GetFullPath($fixtureRoot)
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ($resolvedRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $resolvedRoot)) {
            Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
        }
    }
}

if ($ValidateFixtures) {
    Assert-Gate3SecretAbsenceFixtures
    Write-Output 'BEACON_GATE3_SECRET_FIXTURES_OK'
    exit 0
}

Invoke-Gate3SecretAbsenceCheck -EvidenceDirectory $EvidenceDirectory -CanaryManifest $CanaryManifest
