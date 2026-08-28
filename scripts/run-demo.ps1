$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..') -ErrorAction Stop).Path
$env:APPDATA = Join-Path $repositoryRoot '.appdata'
$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.dotnet-cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Push-Location $repositoryRoot
try {
    dotnet run --project src\DesktopManager.App\DesktopManager.App.csproj
    if ($LASTEXITCODE -ne 0) {
        throw "Application exited with code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
