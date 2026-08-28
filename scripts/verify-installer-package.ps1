[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,
    [Parameter(Mandatory)]
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..') -ErrorAction Stop).Path
$resolvedInstaller = (Resolve-Path -LiteralPath $InstallerPath -ErrorAction Stop).Path
$hashPath = $resolvedInstaller + '.sha256'
if (-not (Test-Path -LiteralPath $hashPath -PathType Leaf)) {
    throw "缺少安装器 SHA256 文件：$hashPath"
}
$expectedHash = ((Get-Content -Raw -LiteralPath $hashPath).Trim() -split '\s+')[0]
$actualHash = (Get-FileHash -LiteralPath $resolvedInstaller -Algorithm SHA256).Hash
if (-not [string]::Equals($expectedHash, $actualHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw '安装器 SHA256 校验失败。'
}
$signature = Get-AuthenticodeSignature -LiteralPath $resolvedInstaller
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned) {
    throw "安装器必须保持未签名，实际状态：$($signature.Status)"
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('DesktopManager.SetupTest.' + [guid]::NewGuid().ToString('N'))
$installRoot = Join-Path $testRoot 'Installed'
$settingsRoot = Join-Path $testRoot 'Settings'
$installLog = Join-Path $testRoot 'install.log'
$upgradeLog = Join-Path $testRoot 'upgrade.log'
$uninstallLog = Join-Path $testRoot 'uninstall.log'

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $settingsRoot -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $settingsRoot 'preserve.txt'), 'preserve-me')

    $installProcess = Start-Process -FilePath $resolvedInstaller -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS',
        "/DIR=`"$installRoot`"", "/LOG=`"$installLog`"") -Wait -PassThru -WindowStyle Hidden
    if ($installProcess.ExitCode -ne 0) {
        $details = if (Test-Path -LiteralPath $installLog) { (Get-Content -LiteralPath $installLog -Tail 30) -join [Environment]::NewLine } else { '未生成安装日志。' }
        throw "静默安装失败：$($installProcess.ExitCode)$([Environment]::NewLine)$details"
    }
    $installedExecutable = Join-Path $installRoot 'DesktopManager.App.exe'
    $uninstaller = Join-Path $installRoot 'unins000.exe'
    foreach ($required in @($installedExecutable, $uninstaller)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "安装结果缺少文件：$required"
        }
    }
    if (-not (Get-Item -LiteralPath $installedExecutable).VersionInfo.ProductVersion.StartsWith($ExpectedVersion, [StringComparison]::Ordinal)) {
        throw '安装后的应用版本不正确。'
    }

    & (Join-Path $repositoryRoot 'scripts\test-collection-windows-smoke.ps1') -ApplicationPath $installedExecutable

    $upgradeProcess = Start-Process -FilePath $resolvedInstaller -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS',
        "/DIR=`"$installRoot`"", "/LOG=`"$upgradeLog`"") -Wait -PassThru -WindowStyle Hidden
    if ($upgradeProcess.ExitCode -ne 0) { throw "原位升级失败：$($upgradeProcess.ExitCode)" }
    if (-not (Test-Path -LiteralPath (Join-Path $settingsRoot 'preserve.txt') -PathType Leaf)) {
        throw '原位升级不应删除用户设置。'
    }

    $uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        "/LOG=`"$uninstallLog`"") -Wait -PassThru -WindowStyle Hidden
    if ($uninstallProcess.ExitCode -ne 0) { throw "静默卸载失败：$($uninstallProcess.ExitCode)" }
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ((Test-Path -LiteralPath $installRoot) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
    if (Test-Path -LiteralPath $installRoot) {
        throw "卸载后安装目录仍存在：$installRoot"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $settingsRoot 'preserve.txt') -PathType Leaf)) {
        throw '卸载不应删除用户设置。'
    }
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
        $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if (-not $resolvedTestRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "拒绝清理临时目录之外的路径：$resolvedTestRoot"
        }
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}

Write-Host "Installer verification passed: $ExpectedVersion, SHA256, unsigned state, install, upgrade, application smoke and uninstall."
