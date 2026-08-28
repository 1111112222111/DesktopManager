[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "版本号格式无效：$Version"
}
if ($RuntimeIdentifier -notin @('win-x64', 'win-arm64')) {
    throw "当前只支持 win-x64 或 win-arm64：$RuntimeIdentifier"
}
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $workspaceRoot 'artifacts\release'
$publishRoot = Join-Path $releaseRoot ('.publish.' + [guid]::NewGuid().ToString('N'))
$packageName = "DesktopManager-$Version-$RuntimeIdentifier"
$packageRoot = Join-Path $releaseRoot $packageName
$archivePath = Join-Path $releaseRoot ($packageName + '.zip')
$hashPath = $archivePath + '.sha256'

function Remove-SafeReleaseArtifact {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedReleaseRoot = [System.IO.Path]::GetFullPath($releaseRoot).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($resolvedReleaseRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝删除发行目录之外的路径：$resolvedPath"
    }

    $item = Get-Item -LiteralPath $resolvedPath -Force
    if ($item.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) {
        throw "拒绝删除重解析点：$resolvedPath"
    }
    if ($item.PSIsContainer) {
        $nestedReparsePoint = Get-ChildItem -LiteralPath $resolvedPath -Force -Recurse |
            Where-Object { $_.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint) } |
            Select-Object -First 1
        if ($null -ne $nestedReparsePoint) {
            throw "发行产物包含重解析点，拒绝删除：$($nestedReparsePoint.FullName)"
        }
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
    else {
        Remove-Item -LiteralPath $resolvedPath -Force
    }
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
Remove-SafeReleaseArtifact -Path $packageRoot
Remove-SafeReleaseArtifact -Path $archivePath
Remove-SafeReleaseArtifact -Path $hashPath

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
        throw "发布输出缺少 DesktopManager.App.exe：$publishedExecutable"
    }

    New-Item -ItemType Directory -Path $packageRoot | Out-Null
    Copy-Item -LiteralPath $publishedExecutable -Destination (Join-Path $packageRoot 'DesktopManager.App.exe')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'packaging\Install.cmd') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'packaging\Uninstall.cmd') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'packaging\README.txt') -Destination $packageRoot
    $manifest = [ordered]@{
        version = $Version
        executable = 'DesktopManager.App.exe'
        runtimeIdentifier = $RuntimeIdentifier
        selfContained = $true
        signed = $false
        publishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $packageRoot 'release.json'),
        ($manifest | ConvertTo-Json),
        [System.Text.UTF8Encoding]::new($false))

    Compress-Archive -LiteralPath $packageRoot -DestinationPath $archivePath -CompressionLevel Optimal
    $hash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
    [System.IO.File]::WriteAllText(
        $hashPath,
        ($hash.Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($archivePath) + [Environment]::NewLine),
        [System.Text.UTF8Encoding]::new($false))
}
finally {
    Remove-SafeReleaseArtifact -Path $publishRoot
}

Write-Host "发行包：$archivePath"
Write-Host "SHA256：$($hash.Hash.ToLowerInvariant())"
