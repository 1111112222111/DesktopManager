[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..') -ErrorAction Stop).Path
$pngPath = Join-Path $repositoryRoot 'src\DesktopManager.App\Assets\DesktopManager.png'
$icoPath = Join-Path $repositoryRoot 'src\DesktopManager.App\Assets\DesktopManager.ico'
$projectPath = Join-Path $repositoryRoot 'src\DesktopManager.App\DesktopManager.App.csproj'
$trayPath = Join-Path $repositoryRoot 'src\DesktopManager.App\TrayIconController.cs'

foreach ($path in @($pngPath, $icoPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Application icon asset is missing: $path"
    }
}

$pngBytes = [System.IO.File]::ReadAllBytes($pngPath)
if ($pngBytes.Length -lt 26 `
    -or $pngBytes[0] -ne 0x89 `
    -or $pngBytes[1] -ne 0x50 `
    -or $pngBytes[2] -ne 0x4E `
    -or $pngBytes[3] -ne 0x47 `
    -or $pngBytes[25] -notin @(4, 6)) {
    throw 'The PNG application icon must contain an alpha channel.'
}

$icoBytes = [System.IO.File]::ReadAllBytes($icoPath)
if ($icoBytes.Length -lt 6 `
    -or [BitConverter]::ToUInt16($icoBytes, 0) -ne 0 `
    -or [BitConverter]::ToUInt16($icoBytes, 2) -ne 1 `
    -or [BitConverter]::ToUInt16($icoBytes, 4) -lt 9) {
    throw 'The ICO application icon must contain at least nine Windows sizes.'
}

$projectSource = [System.IO.File]::ReadAllText($projectPath, [System.Text.Encoding]::UTF8)
$traySource = [System.IO.File]::ReadAllText($trayPath, [System.Text.Encoding]::UTF8)
if ($projectSource.IndexOf('<ApplicationIcon>Assets\DesktopManager.ico</ApplicationIcon>', [StringComparison]::Ordinal) -lt 0) {
    throw 'The executable is not bound to the application icon.'
}
if ($traySource.IndexOf('ExtractAssociatedIcon', [StringComparison]::Ordinal) -lt 0) {
    throw 'The tray icon does not reuse the executable icon.'
}

Write-Host 'Application icon verification passed: alpha PNG, 9-size ICO, executable and tray bindings are present.'
