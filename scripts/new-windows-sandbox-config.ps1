[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [string]$OutputPath,
    [string]$ResultDirectory
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $workspaceRoot 'artifacts\release\DesktopManager-Smoke.wsb'
}
if ([string]::IsNullOrWhiteSpace($ResultDirectory)) {
    $ResultDirectory = Join-Path $workspaceRoot 'artifacts\sandbox-results'
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$resolvedResultDirectory = [System.IO.Path]::GetFullPath($ResultDirectory)
$mappedRoot = [System.Security.SecurityElement]::Escape($workspaceRoot)
$mappedResultRoot = [System.Security.SecurityElement]::Escape($resolvedResultDirectory)
$directorySeparator = [System.IO.Path]::DirectorySeparatorChar
$workspacePrefix = [System.IO.Path]::GetFullPath($workspaceRoot).TrimEnd($directorySeparator) + $directorySeparator
if (-not $resolvedArchive.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The release archive must be inside the workspace for read-only Sandbox mapping.'
}
$archiveRelative = $resolvedArchive.Substring($workspacePrefix.Length)
$sandboxWorkspace = 'C:\DesktopManagerWorkspace'
$sandboxArchive = Join-Path $sandboxWorkspace $archiveRelative
$sandboxResult = 'C:\DesktopManagerResults\DesktopManager-Smoke.json'
$command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$sandboxWorkspace\scripts\windows-sandbox-entry.ps1`" -ArchivePath `"$sandboxArchive`" -ResultPath `"$sandboxResult`""
$escapedCommand = [System.Security.SecurityElement]::Escape($command)
$content = @"
<Configuration>
  <Networking>Disable</Networking>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$mappedRoot</HostFolder>
      <SandboxFolder>$sandboxWorkspace</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$mappedResultRoot</HostFolder>
      <SandboxFolder>C:\DesktopManagerResults</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>$escapedCommand</Command></LogonCommand>
</Configuration>
"@
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedOutput) -Force | Out-Null
New-Item -ItemType Directory -Path $resolvedResultDirectory -Force | Out-Null
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($resolvedOutput, $content, $utf8WithoutBom)
Write-Host "Windows Sandbox config: $resolvedOutput"
