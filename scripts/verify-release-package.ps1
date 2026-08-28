[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ArchivePath,
    [string]$PreviousArchivePath
)

$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )
    if (-not $Condition) {
        throw $Message
    }
}

$resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
$hashPath = $resolvedArchive + '.sha256'
Assert-Condition (Test-Path -LiteralPath $hashPath -PathType Leaf) "缺少 SHA256 文件：$hashPath"
$expectedHash = ((Get-Content -Raw -LiteralPath $hashPath).Trim() -split '\s+')[0]
$actualHash = (Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA256).Hash
Assert-Condition ([string]::Equals($expectedHash, $actualHash, [StringComparison]::OrdinalIgnoreCase)) '发行包 SHA256 校验失败。'

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DesktopManager.PackageTest." + [guid]::NewGuid().ToString('N'))
$extractRoot = Join-Path $testRoot 'Extracted'
$previousExtractRoot = Join-Path $testRoot 'PreviousExtracted'
$installRoot = Join-Path $testRoot 'Installed'
$settingsRoot = Join-Path $testRoot 'Settings'

try {
    New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $settingsRoot -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $settingsRoot 'settings.json'), 'preserve-me')
    Expand-Archive -LiteralPath $resolvedArchive -DestinationPath $extractRoot

    $manifests = @(Get-ChildItem -LiteralPath $extractRoot -Filter 'release.json' -File -Recurse)
    Assert-Condition ($manifests.Count -eq 1) '发行包必须包含且只能包含一个 release.json。'
    $packageRoot = Split-Path -Parent $manifests[0].FullName
    $manifest = Get-Content -Raw -LiteralPath $manifests[0].FullName | ConvertFrom-Json
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($manifest.version)) '发行清单缺少版本号。'
    Assert-Condition ($manifest.selfContained -eq $true) '发行包必须是自包含发布。'
    Assert-Condition (Test-Path -LiteralPath (Join-Path $packageRoot $manifest.executable) -PathType Leaf) '发行包缺少应用程序。'
    Assert-Condition ($manifest.signed -eq $false) '发行清单必须标记为未签名。'

    $installScript = Join-Path $packageRoot 'install.ps1'
    if (-not [string]::IsNullOrWhiteSpace($PreviousArchivePath)) {
        $resolvedPreviousArchive = (Resolve-Path -LiteralPath $PreviousArchivePath).Path
        $previousHashPath = $resolvedPreviousArchive + '.sha256'
        Assert-Condition (Test-Path -LiteralPath $previousHashPath -PathType Leaf) "旧发行包缺少 SHA256 文件：$previousHashPath"
        $expectedPreviousHash = ((Get-Content -Raw -LiteralPath $previousHashPath).Trim() -split '\s+')[0]
        $actualPreviousHash = (Get-FileHash -LiteralPath $resolvedPreviousArchive -Algorithm SHA256).Hash
        Assert-Condition ([string]::Equals($expectedPreviousHash, $actualPreviousHash, [StringComparison]::OrdinalIgnoreCase)) '旧发行包 SHA256 校验失败。'

        New-Item -ItemType Directory -Path $previousExtractRoot -Force | Out-Null
        Expand-Archive -LiteralPath $resolvedPreviousArchive -DestinationPath $previousExtractRoot
        $previousManifests = @(Get-ChildItem -LiteralPath $previousExtractRoot -Filter 'release.json' -File -Recurse)
        Assert-Condition ($previousManifests.Count -eq 1) '旧发行包必须包含且只能包含一个 release.json。'
        $previousPackageRoot = Split-Path -Parent $previousManifests[0].FullName
        $previousManifest = Get-Content -Raw -LiteralPath $previousManifests[0].FullName | ConvertFrom-Json
        Assert-Condition ($previousManifest.version -ne $manifest.version) '升级验收要求旧包与新包版本不同。'

        & (Join-Path $previousPackageRoot 'install.ps1') `
            -PackageRoot $previousPackageRoot `
            -InstallRoot $installRoot `
            -SkipShellIntegration
        [System.IO.File]::WriteAllText((Join-Path $installRoot 'old-install-marker.txt'), 'must-be-replaced')
    }

    & $installScript -PackageRoot $packageRoot -InstallRoot $installRoot -SkipShellIntegration
    Assert-Condition (Test-Path -LiteralPath (Join-Path $installRoot $manifest.executable) -PathType Leaf) '隔离安装失败。'
    $installedManifest = Get-Content -Raw -LiteralPath (Join-Path $installRoot 'release.json') | ConvertFrom-Json
    Assert-Condition ($installedManifest.version -eq $manifest.version) '安装后的版本与当前发行包不一致。'
    $packageExecutableHash = (Get-FileHash -LiteralPath (Join-Path $packageRoot $manifest.executable) -Algorithm SHA256).Hash
    $installedExecutableHash = (Get-FileHash -LiteralPath (Join-Path $installRoot $manifest.executable) -Algorithm SHA256).Hash
    Assert-Condition ($packageExecutableHash -eq $installedExecutableHash) '安装后的可执行文件与当前发行包不一致。'
    Assert-Condition (-not (Test-Path -LiteralPath (Join-Path $installRoot 'old-install-marker.txt'))) '升级没有完整替换旧安装目录。'
    Assert-Condition (Test-Path -LiteralPath (Join-Path $settingsRoot 'settings.json')) '安装或升级不应修改用户设置。'

    & (Join-Path $installRoot 'uninstall.ps1') `
        -InstallRoot $installRoot `
        -SettingsRoot $settingsRoot `
        -SkipShellIntegration
    Assert-Condition (-not (Test-Path -LiteralPath $installRoot)) '隔离卸载后仍存在安装目录。'
    Assert-Condition (Test-Path -LiteralPath (Join-Path $settingsRoot 'settings.json')) '隔离卸载不应删除默认保留的设置。'

    $validationSummary = if ([string]::IsNullOrWhiteSpace($PreviousArchivePath)) {
        'SHA256、结构、安装和卸载'
    }
    else {
        'SHA256、结构、安装、升级和卸载'
    }
    Write-Host "发行包验证通过：版本 $($manifest.version)，${validationSummary}均有效。"
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
