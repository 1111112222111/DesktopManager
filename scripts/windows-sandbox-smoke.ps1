[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [switch]$UseShellIntegrationWrappers
)

$ErrorActionPreference = 'Stop'
# Some automation hosts and fresh Sandbox sessions omit WINDIR. PowerShell
# modules and WPF both require it, so normalize the child-process environment
# before loading archive or UI components.
if ([string]::IsNullOrWhiteSpace($env:WINDIR)) {
    if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) {
        throw 'Neither WINDIR nor SystemRoot is available for Sandbox initialization.'
    }
    $env:WINDIR = $env:SystemRoot
}
$resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
$baseTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$smokeRoot = Join-Path $baseTempRoot ('DesktopManager.SandboxSmoke.' + [guid]::NewGuid().ToString('N'))
$extractRoot = Join-Path $smokeRoot 'Package'
$installRoot = Join-Path $smokeRoot 'Installed'
$settingsRoot = Join-Path $smokeRoot 'Settings'

try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($resolvedArchive, $extractRoot)
    $manifests = @(Get-ChildItem -LiteralPath $extractRoot -Filter release.json -File -Recurse)
    if ($manifests.Count -ne 1) {
        throw 'The release archive must contain exactly one release.json.'
    }
    $manifestPath = $manifests[0].FullName
    $packageRoot = Split-Path -Parent $manifestPath
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.signed -ne $false) {
        throw 'The release manifest must describe an unsigned package.'
    }

    if ($UseShellIntegrationWrappers) {
        $localApplicationData = [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData)
        if ([string]::IsNullOrWhiteSpace($localApplicationData)) {
            throw 'Windows did not return LocalApplicationData inside Sandbox.'
        }
        $installRoot = Join-Path $localApplicationData 'Programs\DesktopManager'
        $env:DESKTOP_MANAGER_NO_PAUSE = '1'
        $installCommand = Join-Path $packageRoot 'Install.cmd'
        $installArguments = "/d /c `"`"$installCommand`"`""
        $installOutput = Join-Path $smokeRoot 'Install.stdout.log'
        $installError = Join-Path $smokeRoot 'Install.stderr.log'
        $installProcess = Start-Process -FilePath $env:ComSpec `
            -ArgumentList $installArguments `
            -WorkingDirectory $packageRoot `
            -RedirectStandardOutput $installOutput `
            -RedirectStandardError $installError `
            -WindowStyle Hidden -Wait -PassThru
        try {
            if ($installProcess.ExitCode -ne 0) {
                $details = ((Get-Content -LiteralPath $installOutput, $installError `
                    -ErrorAction SilentlyContinue) -join [Environment]::NewLine).Trim()
                throw "Install.cmd failed with exit code $($installProcess.ExitCode): $details"
            }
        }
        finally {
            $installProcess.Dispose()
        }
    }
    else {
        & (Join-Path $packageRoot 'install.ps1') `
            -PackageRoot $packageRoot `
            -InstallRoot $installRoot `
            -SkipShellIntegration
    }
    $installedExecutable = Join-Path $installRoot $manifest.executable
    $env:DESKTOP_MANAGER_DATA_ROOT = Join-Path $smokeRoot 'AppData'
    New-Item -ItemType Directory -Path $env:DESKTOP_MANAGER_DATA_ROOT -Force | Out-Null
    $collectionMonitoredRoot = Join-Path $smokeRoot 'CollectionDesktop'
    $collectionManagedRoot = Join-Path $smokeRoot 'CollectionManaged'
    $collectionZoneRoot = Join-Path $collectionManagedRoot '工作文档'
    New-Item -ItemType Directory -Path $collectionMonitoredRoot, $collectionZoneRoot -Force | Out-Null
    $collectionStoredFile = Join-Path $collectionZoneRoot '沙箱窗口验证.txt'
    Set-Content -LiteralPath $collectionStoredFile -Value 'collection window package smoke'
    $collectionSettings = [ordered]@{
        managedDirectory = $collectionManagedRoot
        monitoredDirectory = $collectionMonitoredRoot
        includePublicDesktopReadOnly = $false
        realDesktopWriteAuthorized = $false
        rules = @(
            [ordered]@{
                id = [guid]::NewGuid()
                name = '文档归档'
                priority = 100
                extensions = @('.txt')
                relativeDestination = '工作文档'
                isEnabled = $true
                fileNameKeywords = @()
                itemKinds = @()
            }
        )
        collectionWindows = [ordered]@{ layouts = $null }
    }
    $collectionSettings | ConvertTo-Json -Depth 8 | Set-Content `
        -LiteralPath (Join-Path $env:DESKTOP_MANAGER_DATA_ROOT 'settings.json')
    $process = Start-Process -FilePath $installedExecutable -ArgumentList '--background', '--smoke-test' `
        -WorkingDirectory $installRoot -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(20000)) {
            throw 'The application did not complete its isolated UI smoke test within 20 seconds.'
        }
        if ($process.ExitCode -ne 0) {
            throw "The application smoke test failed with exit code: $($process.ExitCode)"
        }
        $remainingProcesses = @(Get-CimInstance Win32_Process -Filter "Name='DesktopManager.App.exe'" |
            Where-Object {
                $null -ne $_.ExecutablePath -and
                [string]::Equals($_.ExecutablePath, $installedExecutable, [StringComparison]::OrdinalIgnoreCase)
            })
        if ($remainingProcesses.Count -ne 0) {
            throw 'The application left an installed smoke-test process running.'
        }
        if (-not (Test-Path -LiteralPath $collectionStoredFile)) {
            throw 'The collection window package smoke changed its existing stored file.'
        }
    }
    finally {
        $process.Dispose()
    }

    if ($UseShellIntegrationWrappers) {
        $uninstallCommand = Join-Path $installRoot 'Uninstall.cmd'
        $uninstallArguments = "/d /c `"`"$uninstallCommand`"`""
        $uninstallOutput = Join-Path $smokeRoot 'Uninstall.stdout.log'
        $uninstallError = Join-Path $smokeRoot 'Uninstall.stderr.log'
        $uninstallProcess = Start-Process -FilePath $env:ComSpec `
            -ArgumentList $uninstallArguments `
            -WorkingDirectory $smokeRoot `
            -RedirectStandardOutput $uninstallOutput `
            -RedirectStandardError $uninstallError `
            -WindowStyle Hidden -Wait -PassThru
        try {
            if ($uninstallProcess.ExitCode -ne 0) {
                $details = ((Get-Content -LiteralPath $uninstallOutput, $uninstallError `
                    -ErrorAction SilentlyContinue) -join [Environment]::NewLine).Trim()
                throw "Uninstall.cmd failed with exit code $($uninstallProcess.ExitCode): $details"
            }
        }
        finally {
            $uninstallProcess.Dispose()
        }
        $uninstallDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
        while ((Test-Path -LiteralPath $installRoot) `
            -and [DateTimeOffset]::UtcNow -lt $uninstallDeadline) {
            Start-Sleep -Milliseconds 250
        }
    }
    else {
        & (Join-Path $installRoot 'uninstall.ps1') `
            -InstallRoot $installRoot `
            -SettingsRoot $settingsRoot `
            -SkipShellIntegration
    }
    if (Test-Path -LiteralPath $installRoot) {
        throw 'The installation directory remained after Sandbox uninstall.'
    }
    Write-Host "Windows Sandbox smoke test passed: $($manifest.version)"
}
finally {
    if (Test-Path -LiteralPath $smokeRoot) {
        $resolvedSmokeRoot = [System.IO.Path]::GetFullPath($smokeRoot)
        $resolvedTempRoot = $baseTempRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) `
            + [System.IO.Path]::DirectorySeparatorChar
        if (-not $resolvedSmokeRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean a path outside the Sandbox temp directory: $resolvedSmokeRoot"
        }
        Remove-Item -LiteralPath $resolvedSmokeRoot -Recurse -Force
    }
}
