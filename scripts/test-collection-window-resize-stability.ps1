[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..') -ErrorAction Stop).Path
$sourcePath = Join-Path $repositoryRoot 'src\DesktopManager.App\CollectionWindow.xaml.cs'
$source = [System.IO.File]::ReadAllText($sourcePath, [System.Text.Encoding]::UTF8)
$sizeStart = $source.IndexOf('SizeChanged +=', [StringComparison]::Ordinal)
$closedStart = $source.IndexOf('Closed +=', [StringComparison]::Ordinal)
if ($sizeStart -lt 0 -or $closedStart -le $sizeStart) {
    throw 'Unable to locate the collection-window size event block.'
}
$sizeEventBlock = $source.Substring($sizeStart, $closedStart - $sizeStart)
if ($sizeEventBlock.IndexOf('ApplyLiveLayoutCorrection()', [StringComparison]::Ordinal) -ge 0) {
    throw 'Resize stability regression: SizeChanged performs live layout correction and competes with native sizing.'
}
if ($source.IndexOf('LocationChanged +=', [StringComparison]::Ordinal) -lt 0 `
    -or $source.IndexOf('CollectionWindowLayoutChangeKind.MoveCompleted', [StringComparison]::Ordinal) -lt 0) {
    throw 'Move completion regression: release-time layout correction is not wired.'
}
$locationStart = $source.IndexOf('LocationChanged +=', [StringComparison]::Ordinal)
$sizeStartForMoveCheck = $source.IndexOf('SizeChanged +=', $locationStart, [StringComparison]::Ordinal)
$locationBlock = $source.Substring($locationStart, $sizeStartForMoveCheck - $locationStart)
if ($locationBlock.IndexOf('ApplyLayoutCorrection', [StringComparison]::Ordinal) -ge 0) {
    throw 'Move overlap regression: layout correction runs before the drag is released.'
}
if ($source.IndexOf('wmExitSizeMove', [StringComparison]::Ordinal) -lt 0 `
    -or $source.IndexOf('ScheduleLayoutSave()', [StringComparison]::Ordinal) -lt 0) {
    throw 'Resize completion regression: final layout normalization is not scheduled.'
}
if ($source.IndexOf('_cornerResizeUsesWidth', [StringComparison]::Ordinal) -lt 0 `
    -or $source.IndexOf('ApplyNativeSize', [StringComparison]::Ordinal) -lt 0) {
    throw 'Corner resize stability regression: the aspect-ratio driver is not locked for the interaction.'
}
if ($source.IndexOf('CollectionWindowLayoutChangeKind.ResizeLive', [StringComparison]::Ordinal) -lt 0 `
    -or $source.IndexOf('ToResizeEdge', [StringComparison]::Ordinal) -lt 0) {
    throw 'Resize snapping regression: native edge-aware size matching is not wired.'
}

Write-Host 'Collection window resize stability verification passed.'
