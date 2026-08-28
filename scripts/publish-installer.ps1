[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "版本号格式无效：$Version"
}
if ($RuntimeIdentifier -ne 'win-x64') {
    throw "当前安装器只支持 win-x64：$RuntimeIdentifier"
}

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $workspaceRoot 'artifacts\release'
$publishRoot = Join-Path $releaseRoot ('.installer-publish.' + [guid]::NewGuid().ToString('N'))
$outputBaseFilename = "DesktopManager-Setup-$Version-$RuntimeIdentifier"
$installerPath = Join-Path $releaseRoot ($outputBaseFilename + '.exe')
$hashPath = $installerPath + '.sha256'

function Remove-SafeInstallerArtifact {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedReleaseRoot = [System.IO.Path]::GetFullPath($releaseRoot).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($resolvedReleaseRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理发行目录之外的路径：$resolvedPath"
    }
    $item = Get-Item -LiteralPath $resolvedPath -Force
    if ($item.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) {
        throw "拒绝清理重解析点：$resolvedPath"
    }
    if ($item.PSIsContainer) {
        $nestedLink = Get-ChildItem -LiteralPath $resolvedPath -Force -Recurse |
            Where-Object { $_.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint) } |
            Select-Object -First 1
        if ($null -ne $nestedLink) {
            throw "发行暂存目录包含重解析点：$($nestedLink.FullName)"
        }
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
    else {
        Remove-Item -LiteralPath $resolvedPath -Force
    }
}

$compilerCandidates = @(
    (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)
$compilerPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($compilerPath)) {
    throw '未找到 Inno Setup 6 编译器 ISCC.exe。'
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
Remove-SafeInstallerArtifact -Path $publishRoot
Remove-SafeInstallerArtifact -Path $installerPath
Remove-SafeInstallerArtifact -Path $hashPath

try {
    $publishArguments = @(
        'publish',
        (Join-Path $workspaceRoot 'src\DesktopManager.App\DesktopManager.App.csproj'),
        '--configuration', 'Release',
        '--runtime', $RuntimeIdentifier,
        '--self-contained', 'true',
        '--nologo',
        '-p:NuGetAudit=false',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:Version=$Version",
        '--output', $publishRoot
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 失败，退出代码：$LASTEXITCODE"
    }

    $publishedExecutable = Join-Path $publishRoot 'DesktopManager.App.exe'
    if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
        throw "发布输出缺少应用程序：$publishedExecutable"
    }

    $compilerArguments = @(
        "/DAppVersion=$Version",
        "/DSourceExe=$publishedExecutable",
        "/DOutputDir=$releaseRoot",
        "/DOutputBaseFilename=$outputBaseFilename",
        ('/DIconFile=' + (Join-Path $workspaceRoot 'src\DesktopManager.App\Assets\DesktopManager.ico')),
        (Join-Path $workspaceRoot 'packaging\DesktopManager.iss')
    )
    & $compilerPath @compilerArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup 编译失败，退出代码：$LASTEXITCODE"
    }
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "安装器输出缺失：$installerPath"
    }

    $hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
    [System.IO.File]::WriteAllText(
        $hashPath,
        ($hash.Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($installerPath) + [Environment]::NewLine),
        [System.Text.UTF8Encoding]::new($false))
}
finally {
    Remove-SafeInstallerArtifact -Path $publishRoot
}

Write-Host "安装器：$installerPath"
Write-Host "SHA256：$($hash.Hash.ToLowerInvariant())"
