[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..') -ErrorAction Stop).Path
$adaptiveSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\AdaptiveDesktopWindow.cs'),
    [System.Text.Encoding]::UTF8)
$coordinatorSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\DesktopWidgetCoordinator.cs'),
    [System.Text.Encoding]::UTF8)
$collectionCoordinatorSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\CollectionWindowCoordinator.cs'),
    [System.Text.Encoding]::UTF8)
$shortcutViewSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\ShortcutWindow.xaml'),
    [System.Text.Encoding]::UTF8)
$shortcutCodeSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\ShortcutWindow.xaml.cs'),
    [System.Text.Encoding]::UTF8)
$calendarViewSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\CalendarWindow.xaml'),
    [System.Text.Encoding]::UTF8)
$calendarCodeSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\CalendarWindow.xaml.cs'),
    [System.Text.Encoding]::UTF8)
$todoViewSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\TodoWindow.xaml'),
    [System.Text.Encoding]::UTF8)
$todoCodeSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\TodoWindow.xaml.cs'),
    [System.Text.Encoding]::UTF8)
$todoDialogViewSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\TodoItemDialog.xaml'),
    [System.Text.Encoding]::UTF8)
$collectionViewSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\CollectionWindow.xaml'),
    [System.Text.Encoding]::UTF8)
$collectionCodeSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\CollectionWindow.xaml.cs'),
    [System.Text.Encoding]::UTF8)
$collectionSettingsViewSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\CollectionWindowSettingsDialog.xaml'),
    [System.Text.Encoding]::UTF8)
$collectionSettingsCodeSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\CollectionWindowSettingsDialog.xaml.cs'),
    [System.Text.Encoding]::UTF8)
$collectionMaterialSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\CollectionWindowMaterialRenderer.cs'),
    [System.Text.Encoding]::UTF8)
$wallpaperPaletteSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\DesktopWallpaperPaletteProvider.cs'),
    [System.Text.Encoding]::UTF8)
$collectionCoreSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.Core\CollectionZones.cs'),
    [System.Text.Encoding]::UTF8)
$shortcutDialogViewSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\ShortcutTargetDialog.xaml'),
    [System.Text.Encoding]::UTF8)
$shortcutDialogCodeSource = [System.IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'src\DesktopManager.App\ShortcutTargetDialog.xaml.cs'),
    [System.Text.Encoding]::UTF8)

foreach ($required in @(
    'wmExitSizeMove',
    'CollectionWindowLayoutChangeKind.ResizeLive',
    '_cornerUsesWidth',
    'ApplyNativeSize',
    'BeginAdaptiveDrag')) {
    if ($adaptiveSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Special-window adaptive layout contract is missing: $required"
    }
}
if ($collectionMaterialSource.IndexOf('new WpfPoint(0, 1)', [StringComparison]::Ordinal) -lt 0) {
    throw 'Collection-window gradient must run vertically from top to bottom.'
}
foreach ($required in @('OrderByDescending(color => Distance(primary, color))', 'Take(24)')) {
    if ($wallpaperPaletteSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Desktop wallpaper palette contrast contract is missing: $required"
    }
}
if ($adaptiveSource.IndexOf('LocationChanged +=', [StringComparison]::Ordinal) -ge 0) {
    throw 'Special-window move regression: layout correction must not compete with live movement.'
}
foreach ($required in @(
    'CollectionWindowLayoutSolver.ResolveMoved',
    'CollectionWindowLayoutSolver.ResolveResized',
    'ExternalObstaclesProvider',
    'EffectiveShortcutWindow',
    'EffectiveTodo',
    'TodoWindow',
    'CaptureCurrentPreferences',
    'FlushCurrentState')) {
    if ($coordinatorSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Special-window coordinator contract is missing: $required"
    }
}
if ($collectionCoordinatorSource.IndexOf('ExternalObstaclesProvider', [StringComparison]::Ordinal) -lt 0) {
    throw 'Collection windows no longer account for special-window obstacles.'
}

$shortcutTitleTransparent = $shortcutViewSource.IndexOf('x:Name="TitleBar" Background="Transparent"', [StringComparison]::Ordinal) -ge 0
$calendarTitleTransparent = $calendarViewSource.IndexOf('x:Name="TitleBar" Background="Transparent"', [StringComparison]::Ordinal) -ge 0
if (-not $shortcutTitleTransparent -or -not $calendarTitleTransparent) {
    throw 'Special-window title bars must expose their full transparent area as a move handle.'
}
if ($todoViewSource.IndexOf('x:Name="TitleBar"', [StringComparison]::Ordinal) -lt 0 `
    -or $todoViewSource.IndexOf('Background="Transparent" Cursor="SizeAll"', [StringComparison]::Ordinal) -lt 0) {
    throw 'Todo title bar must expose its transparent area as a move handle.'
}
foreach ($forbidden in @('SelectionMarquee', 'PreviewMouseMove="ContentPanel_', 'PreviewMouseLeftButtonUp="ContentPanel_')) {
    if ($shortcutViewSource.IndexOf($forbidden, [StringComparison]::Ordinal) -ge 0) {
        throw "Quick-app blank content must not start window movement or marquee selection: $forbidden"
    }
}
$shortcutContentStart = $shortcutCodeSource.IndexOf('ContentPanel_', [StringComparison]::Ordinal)
$shortcutCapturesContent = $shortcutCodeSource.IndexOf('ContentPanel.CaptureMouse()', [StringComparison]::Ordinal) -ge 0
$shortcutDragsFromContent = $shortcutContentStart -ge 0 -and $shortcutCodeSource.IndexOf('BeginAdaptiveDrag', $shortcutContentStart, [StringComparison]::Ordinal) -ge 0
if ($shortcutCapturesContent -or $shortcutDragsFromContent) {
    throw 'Quick-app blank content still contains a drag gesture.'
}
$calendarContentViewDrag = $calendarViewSource.IndexOf('CalendarContent_MouseLeftButtonDown', [StringComparison]::Ordinal) -ge 0
$calendarContentCodeDrag = $calendarCodeSource.IndexOf('CalendarContent_MouseLeftButtonDown', [StringComparison]::Ordinal) -ge 0
if ($calendarContentViewDrag -or $calendarContentCodeDrag) {
    throw 'Calendar content must not move the window.'
}
if ($todoCodeSource.IndexOf('BeginAdaptiveDrag', [StringComparison]::Ordinal) -lt 0 `
    -or $todoCodeSource.IndexOf('CaptureDefinition', [StringComparison]::Ordinal) -lt 0) {
    throw 'Todo window is missing adaptive movement or persistent capture.'
}
foreach ($required in @(
    'QuickAddText_KeyDown',
    'DateChoice_Click',
    'RefreshDateChoices',
    '_selectedDate = DateOnly.FromDateTime(DateTime.Today)',
    'item.DueDate == _selectedDate',
    'HasItems',
    'TodoItemQuery.Apply',
    'SetCompleted',
    'ClearCompleted_Click',
    'Key.Delete',
    'Key.F2',
    'Key.Space')) {
    if ($todoCodeSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Todo interaction contract is missing: $required"
    }
}
foreach ($required in @(
    'x:Name="DateChoices"',
    'GlassDangerTextBrush',
    'Foreground="{StaticResource GlassTextBrush}" Text="{Binding Name}"',
    '<ListBox.GroupStyle>',
    'VirtualizingPanel.IsVirtualizingWhenGrouping="True"',
    'ScrollViewer.HorizontalScrollBarVisibility="Disabled"',
    'x:Name="TasksList"')) {
    if ($todoViewSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Todo view contract is missing: $required"
    }
}
foreach ($forbidden in @(
    'x:Name="PendingFilter"',
    'x:Name="AllFilter"',
    'x:Name="CompletedFilter"',
    'PriorityText',
    'PriorityCombo',
    'StatusText')) {
    if ($todoViewSource.IndexOf($forbidden, [StringComparison]::Ordinal) -ge 0 `
        -or $todoDialogViewSource.IndexOf($forbidden, [StringComparison]::Ordinal) -ge 0) {
        throw "Todo view still contains removed filtering or priority UI: $forbidden"
    }
}
if ($todoDialogViewSource.IndexOf('<ScrollViewer', [StringComparison]::Ordinal) -ge 0 `
    -or $todoDialogViewSource.IndexOf('IsDefault="True"', [StringComparison]::Ordinal) -lt 0) {
    throw 'Todo edit dialog must be compact, non-scrolling, and keyboard-confirmable.'
}

foreach ($required in @(
    'PreviewMouseMove="ContentPanel_PreviewMouseMove"',
    'PreviewMouseLeftButtonUp="ContentPanel_PreviewMouseLeftButtonUp"',
    'x:Name="SelectionMarquee"',
    'SelectionMode="Extended"')) {
    if ($collectionViewSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Collection-window marquee contract is missing: $required"
    }
}
foreach ($required in @(
    'ContentPanel.CaptureMouse()',
    'SystemParameters.MinimumHorizontalDragDistance',
    'IntersectsWith')) {
    if ($collectionCodeSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Collection-window marquee implementation is missing: $required"
    }
}
$contentHandlerStart = $collectionCodeSource.IndexOf('private void ContentPanel_MouseLeftButtonDown', [StringComparison]::Ordinal)
$nextHandlerStart = if ($contentHandlerStart -ge 0) {
    $collectionCodeSource.IndexOf('private void ', $contentHandlerStart + 13, [StringComparison]::Ordinal)
} else { -1 }
if ($contentHandlerStart -lt 0 -or $nextHandlerStart -lt 0) {
    throw 'Collection-window content pointer handler is missing.'
}
$contentHandlerSource = $collectionCodeSource.Substring($contentHandlerStart, $nextHandlerStart - $contentHandlerStart)
if ($contentHandlerSource.IndexOf('DragMove', [StringComparison]::Ordinal) -ge 0) {
    throw 'Collection-window content regression: blank-area drag must select instead of moving the window.'
}

if ($collectionViewSource.IndexOf('ItemCountText', [StringComparison]::Ordinal) -ge 0) {
    throw 'Collection-window title must not display item or rule counts.'
}
foreach ($required in @('Click="WindowSettings_Click"')) {
    if ($collectionViewSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Collection-window appearance entry is missing: $required"
    }
}
foreach ($required in @(
    'NativeWindowMaterial.Apply',
    'CollectionWindowFillMode.Gradient',
    'LinearGradientBrush',
    'SolidColorBrush')) {
    if ($collectionMaterialSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Collection-window optical material implementation is missing: $required"
    }
}
foreach ($required in @(
    '_windowAppearanceOverride',
    'CollectionWindowMaterialRenderer.Apply',
    'CollectionWindowSettingsDialog')) {
    if ($collectionCodeSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Collection-window per-window appearance implementation is missing: $required"
    }
}
foreach ($required in @(
    'x:Name="SolidChoice"',
    'x:Name="GradientChoice"',
    'x:Name="StartColorPreview"',
    'x:Name="EndColorPreview"',
    'Click="ReadDesktopColors_Click"',
    'Click="ApplyAll_Click"',
    'IsDefault="True"')) {
    if ($collectionSettingsViewSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Collection-window appearance dialog contract is missing: $required"
    }
}
if ($collectionSettingsViewSource.IndexOf('x:Name="OpacitySlider"', [StringComparison]::Ordinal) -ge 0) {
    throw 'Per-window appearance dialog must not expose its own opacity control.'
}
foreach ($required in @(
    'UseGlobalAppearance',
    'ApplyToAllWindows',
    'BuildAppearance',
    'DesktopWallpaperPaletteProvider.TryCreateSuggestion',
    'CollectionWindowMaterialRenderer.Apply')) {
    if ($collectionSettingsCodeSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Collection-window appearance dialog behavior is missing: $required"
    }
}
foreach ($required in @(
    'enum CollectionWindowFillMode',
    'Solid = 0',
    'Gradient = 1',
    'GradientEndColor',
    'AdaptiveWindowColors',
    'public static CollectionWindowAppearance Resolve',
    'CollectionWindowAppearance? Appearance = null')) {
    if ($collectionCoreSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Collection-window persisted appearance contract is missing: $required"
    }
}
foreach ($forbidden in @('MaterialInteraction', 'MaterialHighlight', 'MaterialRefraction')) {
    if ($collectionViewSource.IndexOf($forbidden, [StringComparison]::Ordinal) -ge 0) {
        throw "Collection-window must use one plain transparent surface without optical material layers: $forbidden"
    }
}
foreach ($forbidden in @('CreateCrystalFacetBrush', 'CreateDiamondFacetBrush', 'UpdateInteractiveLight')) {
    if ($collectionMaterialSource.IndexOf($forbidden, [StringComparison]::Ordinal) -ge 0) {
        throw "Collection-window renderer still contains a removed material effect: $forbidden"
    }
}
foreach ($required in @('ApplyAppearanceToAllWindows', 'layout with { Appearance = null }')) {
    if ($collectionCoordinatorSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Collection-window apply-all contract is missing: $required"
    }
}
if ($collectionCoordinatorSource.IndexOf('Appearance = existing.Appearance', [StringComparison]::Ordinal) -lt 0) {
    throw 'Collection-window layout reset must preserve its per-window appearance.'
}

foreach ($required in @(
    'x:Name="GroupComboBox"',
    'x:Name="SaveButton"',
    'IsDefault="True"')) {
    if ($shortcutDialogViewSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Quick-app dialog contract is missing: $required"
    }
}
foreach ($forbidden in @('<ScrollViewer', 'x:Name="DialogTitleText"')) {
    if ($shortcutDialogViewSource.IndexOf($forbidden, [StringComparison]::Ordinal) -ge 0) {
        throw "Quick-app dialog must keep its compact form visible without redundant header or scrolling: $forbidden"
    }
}
foreach ($required in @(
    'ShortcutTarget.TryCreate',
    'WindowStartupLocation.CenterScreen')) {
    if ($shortcutDialogCodeSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Quick-app dialog confirmation contract is missing: $required"
    }
}
$groupOpenViewWired = $shortcutViewSource.IndexOf('Click="OpenGroup_Click"', [StringComparison]::Ordinal) -ge 0
$groupOpenCodeWired = $shortcutCodeSource.IndexOf('private void OpenGroup_Click', [StringComparison]::Ordinal) -ge 0
if (-not $groupOpenViewWired -or -not $groupOpenCodeWired) {
    throw 'Quick-app group header is missing its one-click open action.'
}
foreach ($required in @(
    'PropertyGroupDescription',
    'GroupName')) {
    if ($shortcutViewSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        if ($shortcutCodeSource.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "Quick-app grouping contract is missing: $required"
        }
    }
}

Write-Host 'Desktop widget uniqueness and adaptive layout contract verification passed.'
