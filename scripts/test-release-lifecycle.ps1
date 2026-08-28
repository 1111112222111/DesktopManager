param()

$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,
        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$installScript = Join-Path $PSScriptRoot 'install.ps1'
$uninstallScript = Join-Path $PSScriptRoot 'uninstall.ps1'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DesktopManager.ReleaseTest." + [guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $testRoot 'Package'
$installRoot = Join-Path $testRoot 'Installed'
$settingsRoot = Join-Path $testRoot 'Settings'

try {
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $settingsRoot -Force | Out-Null
    [System.IO.File]::WriteAllBytes((Join-Path $packageRoot 'DesktopManager.App.exe'), [byte[]](1, 2, 3))
    [System.IO.File]::WriteAllText(
        (Join-Path $packageRoot 'release.json'),
        '{"version":"1.0.0","executable":"DesktopManager.App.exe"}')
    Copy-Item -LiteralPath $uninstallScript -Destination (Join-Path $packageRoot 'uninstall.ps1')
    [System.IO.File]::WriteAllText((Join-Path $settingsRoot 'settings.json'), 'preserve-me')

    & $installScript -PackageRoot $packageRoot -InstallRoot $installRoot -SkipShellIntegration
    Assert-Condition (Test-Path -LiteralPath (Join-Path $installRoot 'DesktopManager.App.exe')) '安装后缺少应用程序。'

    [System.IO.File]::WriteAllBytes((Join-Path $packageRoot 'DesktopManager.App.exe'), [byte[]](4, 5, 6, 7))
    [System.IO.File]::WriteAllText(
        (Join-Path $packageRoot 'release.json'),
        '{"version":"1.1.0","executable":"DesktopManager.App.exe"}')
    & $installScript -PackageRoot $packageRoot -InstallRoot $installRoot -SkipShellIntegration
    $installedBytes = [System.IO.File]::ReadAllBytes((Join-Path $installRoot 'DesktopManager.App.exe'))
    Assert-Condition ($installedBytes.Length -eq 4) '升级没有替换应用程序。'
    Assert-Condition ((Get-Content -Raw -LiteralPath (Join-Path $settingsRoot 'settings.json')) -eq 'preserve-me') '升级不应修改用户设置。'

    $installedUninstaller = Join-Path $installRoot 'uninstall.ps1'
    & $installedUninstaller -InstallRoot $installRoot -SettingsRoot $settingsRoot -SkipShellIntegration
    Assert-Condition (-not (Test-Path -LiteralPath $installRoot)) '卸载后仍存在应用目录。'
    Assert-Condition (Test-Path -LiteralPath (Join-Path $settingsRoot 'settings.json')) '默认卸载应保留用户设置。'

    Write-Host '发行生命周期验证通过：安装、升级、默认保留设置和卸载均符合预期。'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
        $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if (-not $resolvedTestRoot.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "拒绝清理临时目录之外的路径：$resolvedTestRoot"
        }

        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
