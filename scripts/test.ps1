$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..') -ErrorAction Stop).Path
$env:APPDATA = Join-Path $repositoryRoot '.appdata'
$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.dotnet-cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Push-Location $repositoryRoot
try {
    dotnet test DesktopManager.sln --nologo -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed with exit code $LASTEXITCODE"
    }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot 'test-folder-organization-responsiveness.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "Folder organization responsiveness test failed with exit code $LASTEXITCODE"
    }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot 'test-installer-environment.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "Installer environment tests failed with exit code $LASTEXITCODE"
    }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot 'test-collection-windows-smoke.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "Collection window smoke tests failed with exit code $LASTEXITCODE"
    }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot 'test-desktop-widget-layout-contract.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "Desktop widget layout contract tests failed with exit code $LASTEXITCODE"
    }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot 'verify-application-icon.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "Application icon verification failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
