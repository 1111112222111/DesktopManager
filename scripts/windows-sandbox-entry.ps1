[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][string]$ResultPath
)

$ErrorActionPreference = 'Stop'
$resultRoot = 'C:\DesktopManagerResults'
$resolvedResult = [System.IO.Path]::GetFullPath($ResultPath)
$resultPrefix = [System.IO.Path]::GetFullPath($resultRoot).TrimEnd('\') + '\'
if (-not $resolvedResult.StartsWith($resultPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The result path must be inside the dedicated Sandbox result mapping.'
}

$result = [ordered]@{
    success = $false
    completedAtUtc = $null
    archiveSha256 = $null
    signatureValidation = $false
    shellWrappers = $true
    collectionWindows = $true
    output = $null
    error = $null
}

try {
    $resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $archiveStream = [System.IO.File]::OpenRead($resolvedArchive)
        try {
            $archiveHash = $sha256.ComputeHash($archiveStream)
        }
        finally {
            $archiveStream.Dispose()
        }
    }
    finally {
        $sha256.Dispose()
    }
    $result.archiveSha256 = ([BitConverter]::ToString($archiveHash)).Replace('-', '').ToLowerInvariant()

    $smokeScript = 'C:\DesktopManagerWorkspace\scripts\windows-sandbox-smoke.ps1'
    $output = & $smokeScript `
        -ArchivePath $resolvedArchive `
        -UseShellIntegrationWrappers 2>&1 | Out-String
    $result.success = $true
    $result.output = $output.Trim()
}
catch {
    $result.error = $_.Exception.ToString()
}
finally {
    $result.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedResult) -Force | Out-Null
    $json = $result | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText(
        $resolvedResult,
        $json,
        (New-Object System.Text.UTF8Encoding($false)))
}

if (-not $result.success) {
    exit 1
}
