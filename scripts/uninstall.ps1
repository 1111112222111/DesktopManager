[CmdletBinding()]
param(
    [string]$InstallRoot,
    [string]$SettingsRoot,
    [switch]$RemoveUserData,
    [switch]$SkipShellIntegration
)

$ErrorActionPreference = 'Stop'

function Get-RequiredScriptDirectoryPath {
    $scriptPath = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        $scriptPath = $MyInvocation.ScriptName
    }
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        throw 'Windows PowerShell 未返回卸载脚本路径，无法确定安装目录。'
    }

    return Split-Path -Parent ([System.IO.Path]::GetFullPath($scriptPath))
}

function Get-RequiredLocalApplicationDataPath {
    $path = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw 'Windows 未返回当前用户的 LocalAppData 目录，无法确定默认设置位置。'
    }

    return [System.IO.Path]::GetFullPath($path)
}

if ([string]::IsNullOrWhiteSpace($SettingsRoot)) {
    $SettingsRoot = Join-Path (Get-RequiredLocalApplicationDataPath) 'DesktopManager'
}
if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Get-RequiredScriptDirectoryPath
}

function Get-RequiredProgramsDirectoryPath {
    $path = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    if ([string]::IsNullOrWhiteSpace($path)) {
        $applicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
        if (-not [string]::IsNullOrWhiteSpace($applicationData)) {
            $path = Join-Path $applicationData 'Microsoft\Windows\Start Menu\Programs'
        }
    }
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw 'Windows 未返回当前用户的开始菜单 Programs 目录，无法清理快捷方式。'
    }

    return [System.IO.Path]::GetFullPath($path)
}

function Get-SafeRemovalTarget {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Purpose
    )

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($resolved)
    $forbidden = @(
        $root,
        [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile),
        [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($forbidden | Where-Object { [string]::Equals($resolved.TrimEnd('\'), $_.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase) }) {
        throw "$Purpose 不能是系统根目录、用户目录或 LocalAppData 根目录：$resolved"
    }

    if (Test-Path -LiteralPath $resolved) {
        $item = Get-Item -LiteralPath $resolved -Force
        if (-not $item.PSIsContainer -or $item.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) {
            throw "$Purpose 必须是普通目录，不能是文件、符号链接或联接：$resolved"
        }

        $nestedReparsePoint = Get-ChildItem -LiteralPath $resolved -Force -Recurse |
            Where-Object { $_.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint) } |
            Select-Object -First 1
        if ($null -ne $nestedReparsePoint) {
            throw "$Purpose 包含符号链接或联接，已拒绝删除：$($nestedReparsePoint.FullName)"
        }
    }

    return $resolved
}

$resolvedInstallRoot = Get-SafeRemovalTarget -Path $InstallRoot -Purpose '安装目录'
$resolvedSettingsRoot = Get-SafeRemovalTarget -Path $SettingsRoot -Purpose '设置目录'
$installedExecutable = Join-Path $resolvedInstallRoot 'DesktopManager.App.exe'

$runningInstance = Get-Process -Name 'DesktopManager.App' -ErrorAction SilentlyContinue |
    Where-Object {
        try {
            [string]::Equals($_.Path, $installedExecutable, [StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    } |
    Select-Object -First 1
if ($null -ne $runningInstance) {
    throw '桌面管理仍在运行。请从托盘菜单选择“退出应用”后重新卸载。'
}

if (-not $SkipShellIntegration) {
    $runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        'Software\Microsoft\Windows\CurrentVersion\Run',
        $true)
    try {
        if ($null -ne $runKey) {
            $runKey.DeleteValue('DesktopManager', $false)
        }
    }
    finally {
        if ($null -ne $runKey) {
            $runKey.Dispose()
        }
    }

    $uninstallParent = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        'Software\Microsoft\Windows\CurrentVersion\Uninstall',
        $true)
    try {
        if ($null -ne $uninstallParent) {
            $uninstallParent.DeleteSubKeyTree('DesktopManager', $false)
        }
    }
    finally {
        if ($null -ne $uninstallParent) {
            $uninstallParent.Dispose()
        }
    }

    $shortcutPath = Join-Path (Get-RequiredProgramsDirectoryPath) '桌面管理.lnk'
    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
    }
}

if (Test-Path -LiteralPath $resolvedInstallRoot) {
    Remove-Item -LiteralPath $resolvedInstallRoot -Recurse -Force
}

if ($RemoveUserData -and (Test-Path -LiteralPath $resolvedSettingsRoot)) {
    Remove-Item -LiteralPath $resolvedSettingsRoot -Recurse -Force
    Write-Host "已删除应用文件与用户设置：$resolvedSettingsRoot"
}
else {
    Write-Host "已卸载应用；用户设置已保留：$resolvedSettingsRoot"
}
