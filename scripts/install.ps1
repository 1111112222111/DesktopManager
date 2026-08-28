[CmdletBinding()]
param(
    [string]$PackageRoot,
    [string]$InstallRoot,
    [switch]$SkipShellIntegration
)

$ErrorActionPreference = 'Stop'

function Get-RequiredScriptDirectoryPath {
    $scriptPath = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        $scriptPath = $MyInvocation.ScriptName
    }
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        throw 'Windows PowerShell 未返回安装脚本路径，无法确定发行包目录。'
    }

    return Split-Path -Parent ([System.IO.Path]::GetFullPath($scriptPath))
}

function Get-RequiredLocalApplicationDataPath {
    $path = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw 'Windows 未返回当前用户的 LocalAppData 目录，无法确定默认安装位置。'
    }

    return [System.IO.Path]::GetFullPath($path)
}

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Join-Path (Get-RequiredLocalApplicationDataPath) 'Programs\DesktopManager'
}
if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
    $PackageRoot = Get-RequiredScriptDirectoryPath
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
        throw 'Windows 未返回当前用户的开始菜单 Programs 目录，无法配置快捷方式。'
    }

    return [System.IO.Path]::GetFullPath($path)
}

function Get-ValidatedDirectoryPath {
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
        if (-not $item.PSIsContainer) {
            throw "$Purpose 必须是目录：$resolved"
        }

        if ($item.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) {
            throw "$Purpose 不能是符号链接或联接：$resolved"
        }
    }

    return $resolved
}

function Assert-NoReparsePoints {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Purpose
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $reparsePoint = Get-ChildItem -LiteralPath $Path -Force -Recurse |
        Where-Object { $_.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint) } |
        Select-Object -First 1
    if ($null -ne $reparsePoint) {
        throw "$Purpose 包含符号链接或联接，已拒绝处理：$($reparsePoint.FullName)"
    }
}

function Remove-ShellIntegration {
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

function Set-ShellIntegration {
    param(
        [Parameter(Mandatory)]
        [string]$InstalledExecutable,
        [Parameter(Mandatory)]
        [string]$InstalledUninstaller,
        [Parameter(Mandatory)]
        [string]$Version
    )

    $programsDirectory = Get-RequiredProgramsDirectoryPath
    New-Item -ItemType Directory -Path $programsDirectory -Force | Out-Null
    $shortcutPath = Join-Path $programsDirectory '桌面管理.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $InstalledExecutable
    $shortcut.WorkingDirectory = Split-Path -Parent $InstalledExecutable
    $shortcut.Description = '桌面管理'
    $shortcut.Save()

    $uninstallKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey(
        'Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopManager',
        $true)
    try {
        $uninstallKey.SetValue('DisplayName', '桌面管理')
        $uninstallKey.SetValue('DisplayVersion', $Version)
        $uninstallKey.SetValue('Publisher', 'DesktopManager')
        $uninstallKey.SetValue('InstallLocation', (Split-Path -Parent $InstalledExecutable))
        $uninstallCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstalledUninstaller`""
        $uninstallKey.SetValue('UninstallString', $uninstallCommand)
        $uninstallKey.SetValue('NoModify', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
        $uninstallKey.SetValue('NoRepair', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
    }
    finally {
        $uninstallKey.Dispose()
    }
}

$resolvedPackageRoot = Get-ValidatedDirectoryPath -Path $PackageRoot -Purpose '发行包目录'
$resolvedInstallRoot = Get-ValidatedDirectoryPath -Path $InstallRoot -Purpose '安装目录'
Assert-NoReparsePoints -Path $resolvedPackageRoot -Purpose '发行包目录'
Assert-NoReparsePoints -Path $resolvedInstallRoot -Purpose '现有安装目录'

$manifestPath = Join-Path $resolvedPackageRoot 'release.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "发行包缺少 release.json：$manifestPath"
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($manifest.version) -or [string]::IsNullOrWhiteSpace($manifest.executable)) {
    throw 'release.json 必须包含 version 和 executable。'
}

if ([System.IO.Path]::GetFileName($manifest.executable) -ne $manifest.executable) {
    throw 'release.json 中的 executable 必须是发行包根目录内的文件名。'
}

$packageExecutable = Join-Path $resolvedPackageRoot $manifest.executable
$packageUninstaller = Join-Path $resolvedPackageRoot 'uninstall.ps1'
if (-not (Test-Path -LiteralPath $packageExecutable -PathType Leaf)) {
    throw "发行包缺少应用程序：$packageExecutable"
}
if (-not (Test-Path -LiteralPath $packageUninstaller -PathType Leaf)) {
    throw "发行包缺少卸载脚本：$packageUninstaller"
}

$installParent = Split-Path -Parent $resolvedInstallRoot
New-Item -ItemType Directory -Path $installParent -Force | Out-Null
$stageRoot = Join-Path $installParent ('.DesktopManager.stage.' + [guid]::NewGuid().ToString('N'))
$backupRoot = Join-Path $installParent ('.DesktopManager.backup.' + [guid]::NewGuid().ToString('N'))
$hadExistingInstall = Test-Path -LiteralPath $resolvedInstallRoot

try {
    New-Item -ItemType Directory -Path $stageRoot | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $resolvedPackageRoot -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $stageRoot -Recurse
    }

    if ($hadExistingInstall) {
        Move-Item -LiteralPath $resolvedInstallRoot -Destination $backupRoot
    }

    Move-Item -LiteralPath $stageRoot -Destination $resolvedInstallRoot
    if (-not $SkipShellIntegration) {
        Set-ShellIntegration `
            -InstalledExecutable (Join-Path $resolvedInstallRoot $manifest.executable) `
            -InstalledUninstaller (Join-Path $resolvedInstallRoot 'uninstall.ps1') `
            -Version $manifest.version
    }

    if (Test-Path -LiteralPath $backupRoot) {
        Assert-NoReparsePoints -Path $backupRoot -Purpose '升级备份目录'
        Remove-Item -LiteralPath $backupRoot -Recurse -Force
    }
}
catch {
    if (-not $SkipShellIntegration) {
        Remove-ShellIntegration
    }
    if (Test-Path -LiteralPath $resolvedInstallRoot) {
        Assert-NoReparsePoints -Path $resolvedInstallRoot -Purpose '失败安装目录'
        Remove-Item -LiteralPath $resolvedInstallRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $backupRoot) {
        Move-Item -LiteralPath $backupRoot -Destination $resolvedInstallRoot
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        Assert-NoReparsePoints -Path $stageRoot -Purpose '安装暂存目录'
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
}

Write-Host "桌面管理 $($manifest.version) 已安装到：$resolvedInstallRoot"
