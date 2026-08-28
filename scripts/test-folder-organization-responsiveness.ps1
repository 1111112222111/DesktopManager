$ErrorActionPreference = 'Stop'

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$mainWindowPath = Join-Path $workspaceRoot 'src\DesktopManager.App\MainWindow.xaml.cs'
$organizerPath = Join-Path $workspaceRoot 'src\DesktopManager.Infrastructure\FileOrganizer.cs'

$mainWindowSource = Get-Content -LiteralPath $mainWindowPath -Raw
$organizerSource = Get-Content -LiteralPath $organizerPath -Raw

$executeHandler = [regex]::Match(
    $mainWindowSource,
    'private async void Execute_Click[\s\S]*?\r?\n    private async void Undo_Click')
if (-not $executeHandler.Success) {
    throw 'Execute_Click was not found. Update this responsiveness check with the execution entry point.'
}

if ($executeHandler.Value -match '(?m)^\s*var preflight = organizer\.Inspect\(') {
    throw 'Folder preflight still runs synchronously on the UI thread.'
}

$executeMethod = [regex]::Match(
    $organizerSource,
    'public async Task<OrganizationOperation> ExecuteAsync[\s\S]*?\r?\n    public async Task<OrganizationOperation> UndoAsync')
if (-not $executeMethod.Success) {
    throw 'FileOrganizer.ExecuteAsync was not found. Update this responsiveness check with the executor.'
}

if ($executeMethod.Value -match '(?m)^\s*MovePath\(') {
    throw 'Physical file movement still runs directly on the caller thread.'
}

Write-Host 'Folder organization responsiveness boundary passed.'
