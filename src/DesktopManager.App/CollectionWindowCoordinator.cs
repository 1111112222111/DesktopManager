using System.Windows;
using System.IO;
using DesktopManager.Core;
using DesktopManager.Infrastructure;

namespace DesktopManager.App;

internal sealed record CollectionWindowSummary(
    Guid ZoneId,
    string Name,
    string RelativeDirectory,
    int RuleCount,
    bool HasEnabledRule,
    bool IsVisible);

internal sealed class CollectionWindowCoordinator : IDisposable
{
    private readonly CollectionZoneStorage _storage = new();
    private readonly Dictionary<Guid, CollectionWindow> _windows = [];
    private string? _managedDirectory;
    private IReadOnlyList<CollectionZone> _zones = [];
    private CollectionWindowsPreferences _preferences = new();
    private bool _synchronizing;

    public event Action<CollectionWindowsPreferences>? PreferencesChanged;
    public event Action? SummariesChanged;
    public event Action<string>? OperationCompleted;
    public Func<IReadOnlyList<LayoutRectangle>>? ExternalObstaclesProvider { get; set; }

    public IReadOnlyList<CollectionWindowSummary> Summaries { get; private set; } = [];
    public bool AreVisibleWindowsDesktopHosted => _windows.Values
        .Where(window => window.IsWindowVisible)
        .All(window => window.IsDesktopHosted);
    public IReadOnlyList<LayoutRectangle> GetVisibleRectangles() => _windows.Values
        .Where(window => window.IsWindowVisible)
        .Select(window =>
        {
            var layout = window.CaptureLayout();
            return new LayoutRectangle(layout.Left, layout.Top, layout.Width, window.IsCollapsed ? 46 : layout.Height);
        })
        .ToArray();

    public void Synchronize(
        string managedDirectory,
        IReadOnlyList<CollectionZone> zones,
        CollectionWindowsPreferences preferences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedDirectory);
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(preferences);
        var preferencesAdjusted = false;
        _synchronizing = true;
        try
        {
            var normalizedManagedDirectory = Path.GetFullPath(managedDirectory);
            var mustRecreate = _windows.Count > 0
                && (!string.Equals(
                        _managedDirectory,
                        normalizedManagedDirectory,
                        StringComparison.OrdinalIgnoreCase)
                    || !ZonesEquivalent(_zones, zones)
                    || !PreferencesEquivalent(_preferences, preferences));
            if (mustRecreate)
            {
                foreach (var window in _windows.Values.ToArray())
                {
                    window.Close();
                }
                _windows.Clear();
            }

            _managedDirectory = normalizedManagedDirectory;
            _zones = zones.ToArray();
            _preferences = preferences;
            var activeIds = zones.Select(zone => zone.Id).ToHashSet();
            foreach (var removed in _windows.Where(pair => !activeIds.Contains(pair.Key)).ToArray())
            {
                removed.Value.Close();
                _windows.Remove(removed.Key);
            }

            var workingArea = SystemParameters.WorkArea;
            for (var index = 0; index < zones.Count; index++)
            {
                var zone = zones[index];
                if (_windows.ContainsKey(zone.Id))
                {
                    continue;
                }
                var layout = preferences.EffectiveLayouts.FirstOrDefault(item => item.ZoneId == zone.Id)
                    ?? CreateDefaultLayout(zone.Id, index, workingArea);
                layout = KeepOnScreen(layout, workingArea);
                var window = new CollectionWindow(
                    zone,
                    _managedDirectory,
                    layout,
                    preferences.EffectiveAppearance,
                    preferences.EffectiveItemOrders.Where(order => order.ZoneId == zone.Id).ToArray(),
                    _storage);
                window.LayoutChanging += Window_LayoutChanging;
                window.LayoutChanged += Window_LayoutChanged;
                window.ItemOrderChanged += Window_ItemOrderChanged;
                window.OperationCompleted += message => OperationCompleted?.Invoke(message);
                window.ApplyAppearanceToAllRequested += ApplyAppearanceToAllWindows;
                _windows.Add(zone.Id, window);
                var normalizedLayout = NormalizeLayout(
                    window,
                    layout,
                    workingArea,
                    new CollectionWindowLayoutChange(CollectionWindowLayoutChangeKind.MoveCompleted));
                if (normalizedLayout != layout)
                {
                    window.ApplyLayout(normalizedLayout);
                    _preferences = _preferences with
                    {
                        Layouts = _preferences.EffectiveLayouts
                            .Where(item => item.ZoneId != normalizedLayout.ZoneId)
                            .Append(normalizedLayout)
                            .ToArray()
                    };
                    preferencesAdjusted = true;
                }
                if (layout.IsVisible)
                {
                    window.Show();
                }
            }
            RefreshSummaries(zones);
        }
        finally
        {
            _synchronizing = false;
        }
        if (preferencesAdjusted)
        {
            PublishPreferences();
        }
    }

    public void ShowAll()
    {
        foreach (var window in _windows.Values)
        {
            window.ApplyVisibility(true);
        }
        ArrangeVisibleWindows();
    }

    public void HideAll()
    {
        foreach (var window in _windows.Values)
        {
            window.ApplyVisibility(false);
        }
    }

    public void SetZoneVisibility(Guid zoneId, bool visible)
    {
        if (!_windows.TryGetValue(zoneId, out var window))
        {
            return;
        }
        window.ApplyVisibility(visible);
        if (visible)
        {
            ArrangeVisibleWindows();
        }
    }

    public void OpenZone(Guid zoneId)
    {
        if (_windows.TryGetValue(zoneId, out var window))
        {
            SetZoneVisibility(zoneId, true);
            window.Activate();
        }
    }

    public void UpdateAppearance(Guid zoneId, string title, string accentColor)
    {
        if (!_windows.TryGetValue(zoneId, out var window))
        {
            return;
        }
        window.ApplyAppearance(title, accentColor);
    }

    public void UpdateWindowAppearance(CollectionWindowAppearance appearance)
    {
        var normalized = appearance.Normalize();
        _preferences = _preferences with { Appearance = normalized };
        foreach (var window in _windows.Values)
        {
            window.ApplyWindowAppearance(normalized);
        }
        PublishPreferences();
    }

    public void ApplyAppearanceToAllWindows(CollectionWindowAppearance appearance)
    {
        var normalized = appearance.Normalize();
        _preferences = _preferences with
        {
            Appearance = normalized,
            Layouts = _preferences.EffectiveLayouts
                .Select(layout => layout with { Appearance = null })
                .ToArray()
        };
        _synchronizing = true;
        try
        {
            foreach (var window in _windows.Values)
            {
                window.ApplyAppearanceAsGlobal(normalized);
            }
        }
        finally
        {
            _synchronizing = false;
        }
        PublishPreferences();
    }

    public void ResetLayout()
    {
        var area = SystemParameters.WorkArea;
        var layouts = _windows.Values
            .OrderBy(window => window.ZoneName, StringComparer.CurrentCultureIgnoreCase)
            .Select((window, index) =>
            {
                var existing = window.CaptureLayout();
                return CreateDefaultLayout(window.ZoneId, index, area) with
                {
                    IsVisible = existing.IsVisible,
                    ViewMode = existing.ViewMode,
                    AccentColor = existing.AccentColor,
                    Title = existing.Title,
                    Appearance = existing.Appearance
                };
            })
            .ToArray();
        _preferences = _preferences with { Layouts = layouts };

        _synchronizing = true;
        try
        {
            foreach (var window in _windows.Values.ToArray())
            {
                window.Close();
            }
            _windows.Clear();
        }
        finally
        {
            _synchronizing = false;
        }
        if (_managedDirectory is not null)
        {
            Synchronize(_managedDirectory, _zones, _preferences);
        }
        PublishPreferences();
    }

    public void RefreshAll()
    {
        foreach (var window in _windows.Values)
        {
            window.RefreshItems();
        }
    }

    public void Clear()
    {
        _synchronizing = true;
        try
        {
            foreach (var window in _windows.Values.ToArray())
            {
                window.Close();
            }
            _windows.Clear();
            _zones = [];
            Summaries = [];
        }
        finally
        {
            _synchronizing = false;
        }
        SummariesChanged?.Invoke();
    }

    private void Window_LayoutChanged(CollectionWindow window, CollectionWindowLayout layout)
    {
        if (_synchronizing)
        {
            return;
        }
        var normalized = KeepOnScreen(layout, SystemParameters.WorkArea);
        if (normalized != layout)
        {
            _synchronizing = true;
            try
            {
                window.ApplyLayout(normalized);
            }
            finally
            {
                _synchronizing = false;
            }
        }
        var layouts = _preferences.EffectiveLayouts
            .Where(item => item.ZoneId != normalized.ZoneId)
            .Append(normalized)
            .ToArray();
        _preferences = _preferences with { Layouts = layouts };
        PublishPreferences();
        RefreshSummaryVisibility();
    }

    private void Window_ItemOrderChanged(CollectionWindow window, CollectionWindowItemOrder order)
    {
        if (_synchronizing)
        {
            return;
        }
        _preferences = _preferences with
        {
            ItemOrders = _preferences.EffectiveItemOrders
                .Where(item => item.ZoneId != order.ZoneId
                    || !string.Equals(item.RelativeDirectory, order.RelativeDirectory, StringComparison.OrdinalIgnoreCase))
                .Append(order)
                .ToArray()
        };
        PublishPreferences();
    }

    private CollectionWindowLayout Window_LayoutChanging(
        CollectionWindow window,
        CollectionWindowLayout proposedLayout,
        CollectionWindowLayoutChange change) =>
        _synchronizing
            ? proposedLayout
            : NormalizeLayout(window, proposedLayout, SystemParameters.WorkArea, change);

    private void ArrangeVisibleWindows()
    {
        _synchronizing = true;
        try
        {
            foreach (var window in _windows.Values.Where(candidate => candidate.IsWindowVisible))
            {
                var layout = window.CaptureLayout();
                var normalized = NormalizeLayout(
                    window,
                    layout,
                    SystemParameters.WorkArea,
                    new CollectionWindowLayoutChange(CollectionWindowLayoutChangeKind.MoveCompleted));
                window.ApplyLayout(normalized);
                _preferences = _preferences with
                {
                    Layouts = _preferences.EffectiveLayouts
                        .Where(item => item.ZoneId != normalized.ZoneId)
                        .Append(normalized)
                        .ToArray()
                };
            }
        }
        finally
        {
            _synchronizing = false;
        }
        PublishPreferences();
    }

    private CollectionWindowLayout NormalizeLayout(
        CollectionWindow window,
        CollectionWindowLayout layout,
        Rect workingArea,
        CollectionWindowLayoutChange change)
    {
        layout = KeepOnScreen(layout, workingArea);
        var height = window.IsCollapsed ? 46 : layout.Height;
        var current = new LayoutRectangle(layout.Left, layout.Top, layout.Width, height);
        var others = _windows.Values
            .Where(candidate => candidate.ZoneId != window.ZoneId && candidate.IsWindowVisible)
            .Select(candidate =>
            {
                var otherLayout = candidate.CaptureLayout();
                return new LayoutRectangle(
                    otherLayout.Left,
                    otherLayout.Top,
                    otherLayout.Width,
                    candidate.IsCollapsed ? 46 : otherLayout.Height);
            })
            .Concat(ExternalObstaclesProvider?.Invoke() ?? [])
            .ToArray();
        var area = new LayoutRectangle(
            workingArea.Left,
            workingArea.Top,
            workingArea.Width,
            workingArea.Height);
        var resolved = change.Kind is CollectionWindowLayoutChangeKind.MoveCompleted
            ? CollectionWindowLayoutSolver.ResolveMoved(
                current,
                others,
                area,
                minimumHeight: window.IsCollapsed ? 46 : 180)
            : CollectionWindowLayoutSolver.ResolveResized(
                current,
                others,
                area,
                change.ResizeEdge,
                preventOverlap: change.Kind is CollectionWindowLayoutChangeKind.ResizeCompleted,
                minimumHeight: window.IsCollapsed ? 46 : 180);

        return layout with
        {
            Left = resolved.Left,
            Top = resolved.Top,
            Width = resolved.Width,
            Height = window.IsCollapsed ? layout.Height : resolved.Height
        };
    }

    private static Rect FindNearestFreeRectangle(Rect current, IReadOnlyList<Rect> obstacles, Rect area, double gap)
    {
        var horizontalCandidates = obstacles
            .SelectMany(obstacle => new[] { obstacle.Right + gap, obstacle.Left - gap - current.Width })
            .Append(area.Left)
            .Append(area.Right - current.Width)
            .Append(current.Left)
            .Distinct();
        var verticalCandidates = obstacles
            .SelectMany(obstacle => new[] { obstacle.Bottom + gap, obstacle.Top - gap - current.Height })
            .Append(area.Top)
            .Append(area.Bottom - current.Height)
            .Append(current.Top)
            .Distinct();
        Rect? best = null;
        var bestDistance = double.MaxValue;
        foreach (var left in horizontalCandidates)
        {
            foreach (var top in verticalCandidates)
            {
                var candidate = new Rect(left, top, current.Width, current.Height);
                if (!area.Contains(candidate.TopLeft)
                    || !area.Contains(candidate.BottomRight)
                    || obstacles.Any(obstacle => RectanglesOverlap(candidate, obstacle)))
                {
                    continue;
                }
                var distance = Math.Abs(candidate.Left - current.Left) + Math.Abs(candidate.Top - current.Top);
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
        }
        return best ?? current;
    }

    private static Rect MoveOutside(Rect current, Rect obstacle, Rect area, double gap)
    {
        var candidates = new[]
        {
            new Rect(obstacle.Left - gap - current.Width, current.Top, current.Width, current.Height),
            new Rect(obstacle.Right + gap, current.Top, current.Width, current.Height),
            new Rect(current.Left, obstacle.Top - gap - current.Height, current.Width, current.Height),
            new Rect(current.Left, obstacle.Bottom + gap, current.Width, current.Height)
        };
        Rect? valid = candidates
            .Where(candidate => area.Contains(candidate.TopLeft) && area.Contains(candidate.BottomRight))
            .OrderBy(candidate => Math.Abs(candidate.Left - current.Left) + Math.Abs(candidate.Top - current.Top))
            .Select(candidate => (Rect?)candidate)
            .FirstOrDefault();
        if (valid is not null)
        {
            return valid.Value;
        }
        return new Rect(
            Math.Clamp(current.Left, area.Left, Math.Max(area.Left, area.Right - current.Width)),
            Math.Clamp(current.Top, area.Top, Math.Max(area.Top, area.Bottom - current.Height)),
            current.Width,
            current.Height);
    }

    private static double SnapToNearest(double value, IReadOnlyList<double> candidates, double threshold)
    {
        var nearest = candidates.OrderBy(candidate => Math.Abs(candidate - value)).FirstOrDefault(value);
        return Math.Abs(nearest - value) <= threshold ? nearest : value;
    }

    private static bool RangesOverlap(double firstStart, double firstEnd, double secondStart, double secondEnd) =>
        firstStart < secondEnd && firstEnd > secondStart;

    private static bool RectanglesOverlap(Rect first, Rect second) =>
        RangesOverlap(first.Left, first.Right, second.Left, second.Right)
        && RangesOverlap(first.Top, first.Bottom, second.Top, second.Bottom);

    private static bool DoubleEquals(double first, double second) => Math.Abs(first - second) < 0.1;

    private CollectionWindowLayout GetLayout(Guid zoneId) =>
        _preferences.EffectiveLayouts.FirstOrDefault(item => item.ZoneId == zoneId)
        ?? _windows[zoneId].CaptureLayout();

    private void PublishPreferences() => PreferencesChanged?.Invoke(_preferences);

    private void RefreshSummaries(IReadOnlyList<CollectionZone> zones)
    {
        Summaries = zones.Select(zone => new CollectionWindowSummary(
            zone.Id,
            zone.Name,
            zone.RelativeDirectory,
            zone.RuleIds.Count,
            zone.HasEnabledRule,
            _windows.TryGetValue(zone.Id, out var window) && window.IsVisible)).ToArray();
        SummariesChanged?.Invoke();
    }

    private void RefreshSummaryVisibility()
    {
        Summaries = Summaries.Select(summary => summary with
        {
            Name = _windows.TryGetValue(summary.ZoneId, out var nameWindow) ? nameWindow.ZoneName : summary.Name,
            IsVisible = _windows.TryGetValue(summary.ZoneId, out var window) && window.IsVisible
        }).ToArray();
        SummariesChanged?.Invoke();
    }

    private static CollectionWindowLayout CreateDefaultLayout(Guid zoneId, int index, Rect area)
    {
        const double width = 360;
        const double height = 300;
        const double spacing = 8;
        var columns = Math.Max(1, (int)((area.Width - spacing) / (width + spacing)));
        var column = index % columns;
        var row = index / columns;
        return new CollectionWindowLayout(
            zoneId,
            area.Left + spacing + column * (width + spacing),
            area.Top + spacing + row * (height + spacing),
            width,
            height);
    }

    private static CollectionWindowLayout KeepOnScreen(CollectionWindowLayout layout, Rect area)
    {
        var width = Math.Clamp(layout.Width, 280, Math.Max(280, area.Width));
        var height = Math.Clamp(layout.Height, 180, Math.Max(180, area.Height));
        var left = Math.Clamp(layout.Left, area.Left, Math.Max(area.Left, area.Right - width));
        var top = Math.Clamp(layout.Top, area.Top, Math.Max(area.Top, area.Bottom - 72));
        return layout with { Left = left, Top = top, Width = width, Height = height };
    }

    private static bool ZonesEquivalent(
        IReadOnlyList<CollectionZone> first,
        IReadOnlyList<CollectionZone> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }
        var secondById = second.ToDictionary(zone => zone.Id);
        return first.All(zone => secondById.TryGetValue(zone.Id, out var candidate)
            && string.Equals(zone.Name, candidate.Name, StringComparison.Ordinal)
            && string.Equals(zone.RelativeDirectory, candidate.RelativeDirectory, StringComparison.OrdinalIgnoreCase)
            && zone.HasEnabledRule == candidate.HasEnabledRule
            && zone.RuleIds.SequenceEqual(candidate.RuleIds));
    }

    private static bool PreferencesEquivalent(
        CollectionWindowsPreferences first,
        CollectionWindowsPreferences second) =>
        first.EffectiveAppearance == second.EffectiveAppearance
        && first.EffectiveLayouts.Count == second.EffectiveLayouts.Count
        && first.EffectiveLayouts.All(layout => second.EffectiveLayouts.Contains(layout))
        && first.EffectiveItemOrders.Count == second.EffectiveItemOrders.Count
        && first.EffectiveItemOrders.All(order => second.EffectiveItemOrders.Any(candidate =>
            candidate.ZoneId == order.ZoneId
            && string.Equals(candidate.RelativeDirectory, order.RelativeDirectory, StringComparison.OrdinalIgnoreCase)
            && candidate.EffectiveItemNames.SequenceEqual(order.EffectiveItemNames, StringComparer.CurrentCultureIgnoreCase)));

    public void Dispose()
    {
        Clear();
    }
}
