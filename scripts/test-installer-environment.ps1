param()

$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$installScript = Join-Path $PSScriptRoot 'install.ps1'
$uninstallScript = Join-Path $PSScriptRoot 'uninstall.ps1'
$allowedRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'DesktopManager.InstallerEnvironmentTests'
$testRoot = Join-Path $allowedRoot ([guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $testRoot 'Package'
$originalLocalAppData = $env:LOCALAPPDATA
$writeBoundary = 'INSTALLER_SAFE_WRITE_BOUNDARY'

try {
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    [System.IO.File]::WriteAllBytes(
        (Join-Path $packageRoot 'DesktopManager.App.exe'),
        [byte[]](1, 2, 3))
    [System.IO.File]::WriteAllText(
        (Join-Path $packageRoot 'release.json'),
        '{"version":"1.0.0","executable":"DesktopManager.App.exe"}')
    Copy-Item -LiteralPath $uninstallScript -Destination (Join-Path $packageRoot 'uninstall.ps1')
    Copy-Item -LiteralPath $installScript -Destination (Join-Path $packageRoot 'install.ps1')

    $env:LOCALAPPDATA = $null
    function global:New-Item {
        param(
            [string]$ItemType,
            [string]$Path,
            [switch]$Force
        )
        throw $writeBoundary
    }

    $reachedWriteBoundary = $false
    try {
        & (Join-Path $packageRoot 'install.ps1') `
            -PackageRoot ([string]::Empty) `
            -SkipShellIntegration
    }
    catch {
        if ($_.Exception.Message -ne $writeBoundary) {
            throw
        }
        $reachedWriteBoundary = $true
    }
    finally {
        Microsoft.PowerShell.Management\Remove-Item Function:\global:New-Item -ErrorAction SilentlyContinue
    }

    if (-not $reachedWriteBoundary) {
        throw 'The installer did not reach the intercepted write boundary.'
    }

    & $uninstallScript `
        -InstallRoot (Join-Path $testRoot 'NonexistentInstall') `
        -SkipShellIntegration

    Write-Host 'Installer environment fallback verification passed.'
}
finally {
    Microsoft.PowerShell.Management\Remove-Item Function:\global:New-Item -ErrorAction SilentlyContinue
    $env:LOCALAPPDATA = $originalLocalAppData
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
        $resolvedAllowedRoot = [System.IO.Path]::GetFullPath($allowedRoot).TrimEnd('\') + '\'
        if (-not $resolvedTestRoot.StartsWith(
                $resolvedAllowedRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean outside the installer test root: $resolvedTestRoot"
        }
        Microsoft.PowerShell.Management\Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
