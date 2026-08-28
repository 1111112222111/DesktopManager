[CmdletBinding()]
param(
    [string]$ApplicationPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:WINDIR)) {
    if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) {
        throw 'Neither WINDIR nor SystemRoot is available for WPF initialization.'
    }
    $env:WINDIR = $env:SystemRoot
}
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..') -ErrorAction Stop).Path
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    'DesktopManager.CollectionWindowSmoke.' + [guid]::NewGuid().ToString('N'))
$dataRoot = Join-Path $temporaryRoot 'AppData'
$monitoredRoot = Join-Path $temporaryRoot 'Desktop'
$managedRoot = Join-Path $temporaryRoot 'Managed'
$zoneRoot = Join-Path $managedRoot 'WorkDocuments'
$defaultApplication = Join-Path $repositoryRoot (
    'src\DesktopManager.App\bin\Debug\net10.0-windows10.0.17763.0\DesktopManager.App.exe')
$application = if ([string]::IsNullOrWhiteSpace($ApplicationPath)) {
    $defaultApplication
} else {
    (Resolve-Path -LiteralPath $ApplicationPath -ErrorAction Stop).Path
}

try {
    New-Item -ItemType Directory -Path $dataRoot, $monitoredRoot, $zoneRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $zoneRoot 'window-smoke.txt') -Value 'collection window smoke'
    $settings = [ordered]@{
        managedDirectory = $managedRoot
        monitoredDirectory = $monitoredRoot
        includePublicDesktopReadOnly = $false
        rules = @(
            [ordered]@{
                id = [guid]::NewGuid()
                name = 'Document Archive'
                priority = 100
                extensions = @('.txt')
                relativeDestination = 'WorkDocuments'
                isEnabled = $true
                fileNameKeywords = @()
                itemKinds = @()
            }
        )
        collectionWindows = [ordered]@{ layouts = $null }
        desktopWidgets = [ordered]@{
            shortcutWindows = @(
                [ordered]@{
                    id = [guid]::NewGuid()
                    name = 'Quick Apps Smoke'
                    targets = @(
                        [ordered]@{
                            id = [guid]::NewGuid()
                            name = 'Window Smoke File'
                            target = (Join-Path $zoneRoot 'window-smoke.txt')
                            kind = 1
                            group = 'Work'
                        }
                    )
                    layout = [ordered]@{ left = 430; top = 80; width = 360; height = 300; isVisible = $true }
                }
            )
            calendar = [ordered]@{
                isEnabled = $true
                layout = [ordered]@{ left = 800; top = 80; width = 420; height = 390; isVisible = $true }
            }
            todo = [ordered]@{
                isEnabled = $true
                selectedFilter = 0
                items = @(
                    [ordered]@{
                        id = [guid]::NewGuid()
                        title = 'Todo Smoke Pending'
                        isCompleted = $false
                        priority = 2
                        dueDate = '2026-08-28'
                        createdAt = '2026-08-27T09:00:00+08:00'
                    },
                    [ordered]@{
                        id = [guid]::NewGuid()
                        title = 'Todo Smoke Completed'
                        isCompleted = $true
                        priority = 1
                        createdAt = '2026-08-26T09:00:00+08:00'
                        completedAt = '2026-08-27T10:00:00+08:00'
                    }
                )
                layout = [ordered]@{ left = 1230; top = 80; width = 390; height = 430; isVisible = $true }
            }
        }
    }
    $settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $dataRoot 'settings.json')

    if ([string]::IsNullOrWhiteSpace($ApplicationPath)) {
        & dotnet build (Join-Path $repositoryRoot 'src\DesktopManager.App\DesktopManager.App.csproj') `
            --no-restore --nologo -p:NuGetAudit=false | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Collection window smoke build failed with exit code $LASTEXITCODE."
        }
    }

    $previousDataRoot = $env:DESKTOP_MANAGER_DATA_ROOT
    try {
        $env:DESKTOP_MANAGER_DATA_ROOT = $dataRoot
        foreach ($launch in 1..2) {
            $process = Start-Process -FilePath $application `
                -ArgumentList '--background', '--smoke-test' `
                -WorkingDirectory (Split-Path -Parent $application) `
                -WindowStyle Hidden -PassThru
            try {
                if (-not $process.WaitForExit(20000)) {
                    throw "Collection window UI smoke launch $launch timed out."
                }
                if ($process.ExitCode -ne 0) {
                    $diagnosticFiles = @(Get-ChildItem -LiteralPath (Join-Path $dataRoot 'Logs') `
                        -Filter '*.jsonl' -File -ErrorAction SilentlyContinue)
                    foreach ($diagnosticFile in $diagnosticFiles) {
                        Get-Content -LiteralPath $diagnosticFile.FullName | Out-Host
                    }
                    throw "Collection window UI smoke launch $launch failed with exit code $($process.ExitCode)."
                }
            }
            finally {
                $process.Dispose()
            }

            $persisted = Get-Content -Raw -LiteralPath (Join-Path $dataRoot 'settings.json') | ConvertFrom-Json
            if ($persisted.desktopWidgets.shortcutWindows.Count -ne 1 `
                -or -not $persisted.desktopWidgets.shortcutWindows[0].isEnabled `
                -or $persisted.desktopWidgets.shortcutWindows[0].targets.Count -ne 1 `
                -or $persisted.desktopWidgets.shortcutWindows[0].targets[0].name -ne 'Window Smoke File' `
                -or $persisted.desktopWidgets.shortcutWindows[0].targets[0].group -ne 'Work' `
                -or $persisted.desktopWidgets.shortcutWindows[0].layout.width -lt 300) {
                throw "Quick-app settings were cleared or disabled after launch $launch."
            }
            if (-not $persisted.desktopWidgets.calendar.isEnabled `
                -or $persisted.desktopWidgets.calendar.layout.width -lt 350) {
                throw "Calendar settings were disabled after launch $launch."
            }
            if (-not $persisted.desktopWidgets.todo.isEnabled `
                -or $persisted.desktopWidgets.todo.items.Count -ne 2 `
                -or $persisted.desktopWidgets.todo.items[0].title -ne 'Todo Smoke Pending' `
                -or $persisted.desktopWidgets.todo.items[1].title -ne 'Todo Smoke Completed' `
                -or -not $persisted.desktopWidgets.todo.items[1].isCompleted `
                -or $persisted.desktopWidgets.todo.layout.width -lt 330) {
                throw "Todo settings were cleared or disabled after launch $launch."
            }
            if ($persisted.desktopWidgets.todo.PSObject.Properties.Name -contains 'selectedFilter' `
                -or $persisted.desktopWidgets.todo.items[0].PSObject.Properties.Name -contains 'priority') {
                throw "Removed todo priority or filter settings survived migration after launch $launch."
            }
        }
    }
    finally {
        $env:DESKTOP_MANAGER_DATA_ROOT = $previousDataRoot
    }

    if (-not (Test-Path -LiteralPath (Join-Path $zoneRoot 'window-smoke.txt'))) {
        throw 'Collection window smoke changed the existing stored file unexpectedly.'
    }
    Write-Host 'Collection window UI smoke verification passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
        $resolvedSystemTemp = ([System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $resolvedTemporaryRoot.StartsWith(
                $resolvedSystemTemp,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean collection window smoke path: $resolvedTemporaryRoot"
        }
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
