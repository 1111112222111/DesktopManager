using System.Windows;
using DesktopManager.Core;

namespace DesktopManager.App;

internal sealed class DesktopWidgetCoordinator : IDisposable
{
    private ShortcutWindow? _shortcut;
    private CalendarWindow? _calendar;
    private TodoWindow? _todo;
    private DesktopWidgetsPreferences _preferences = new();
    private bool _synchronizing;
    private bool _disposed;

    public event Action<DesktopWidgetsPreferences>? PreferencesChanged;
    public Func<IReadOnlyList<LayoutRectangle>>? ExternalObstaclesProvider { get; set; }
    public bool AreVisibleWindowsDesktopHosted => VisibleWindows.All(window => window.IsDesktopHosted);
    public bool IsShortcutEnabled => _preferences.EffectiveShortcutWindow?.IsEnabled is true;
    public bool IsCalendarEnabled => _preferences.EffectiveCalendar.IsEnabled;
    public bool IsTodoEnabled => _preferences.EffectiveTodo.IsEnabled;

    private IEnumerable<AdaptiveDesktopWindow> VisibleWindows =>
        new AdaptiveDesktopWindow?[] { _shortcut, _calendar, _todo }.OfType<AdaptiveDesktopWindow>().Where(window => window.IsVisible);

    public void Synchronize(DesktopWidgetsPreferences preferences)
    {
        _synchronizing = true;
        try
        {
            CloseWindows();
            _preferences = preferences.Normalize();
            var shortcut = _preferences.EffectiveShortcutWindow;
            if (shortcut is not null && (shortcut.Name == "快捷窗口" || shortcut.Name.StartsWith("快捷窗口 ", StringComparison.Ordinal)))
            {
                shortcut = shortcut with { Name = "快速应用" };
                _preferences = _preferences with { ShortcutWindows = [shortcut] };
            }
            if (shortcut?.IsEnabled is true)
            {
                _shortcut = new ShortcutWindow(shortcut);
                Wire(_shortcut);
                _shortcut.DefinitionChanged += UpdateShortcut;
                _shortcut.RemoveRequested += _ => SetShortcutEnabled(false);
                if (shortcut.EffectiveLayout.IsVisible) _shortcut.Show();
            }
            if (_preferences.EffectiveCalendar.IsEnabled)
            {
                _calendar = new CalendarWindow(_preferences.EffectiveCalendar);
                Wire(_calendar);
                _calendar.CloseRequested += () => SetCalendarEnabled(false);
                if (_preferences.EffectiveCalendar.EffectiveLayout.IsVisible) _calendar.Show();
            }
            if (_preferences.EffectiveTodo.IsEnabled)
            {
                _todo = new TodoWindow(_preferences.EffectiveTodo);
                Wire(_todo);
                _todo.DefinitionChanged += UpdateTodo;
                _todo.CloseRequested += () => SetTodoEnabled(false);
                if (_preferences.EffectiveTodo.EffectiveLayout.IsVisible) _todo.Show();
            }
        }
        finally { _synchronizing = false; }
        if (VisibleWindows.Any()) ArrangeVisibleWindows();
    }

    public void ToggleShortcut() => SetShortcutEnabled(!IsShortcutEnabled);

    public void ToggleCalendar() => SetCalendarEnabled(!IsCalendarEnabled);

    public void ToggleTodo() => SetTodoEnabled(!IsTodoEnabled);

    private void SetCalendarEnabled(bool enabled)
    {
        var current = _preferences.EffectiveCalendar;
        _preferences = _preferences with
        {
            Calendar = current with { IsEnabled = enabled, Layout = current.EffectiveLayout with { IsVisible = enabled } }
        };
        Synchronize(_preferences);
        Publish();
    }

    private void SetTodoEnabled(bool enabled)
    {
        var current = _preferences.EffectiveTodo;
        _preferences = _preferences with
        {
            Todo = current with { IsEnabled = enabled, Layout = current.EffectiveLayout with { IsVisible = enabled } }
        };
        Synchronize(_preferences);
        Publish();
    }

    public void ShowAll()
    {
        _shortcut?.ApplyVisibility(true);
        _calendar?.ApplyVisibility(true);
        _todo?.ApplyVisibility(true);
        ArrangeVisibleWindows();
    }

    public void HideAll()
    {
        _shortcut?.ApplyVisibility(false);
        _calendar?.ApplyVisibility(false);
        _todo?.ApplyVisibility(false);
    }

    public void Arrange() => ArrangeVisibleWindows();

    public IReadOnlyList<LayoutRectangle> GetVisibleRectangles() => VisibleWindows
        .Select(window => ToRectangle(window.CaptureAdaptiveLayout()))
        .ToArray();

    private void SetShortcutEnabled(bool enabled)
    {
        var current = _preferences.EffectiveShortcutWindow
            ?? new ShortcutWindowDefinition(Guid.NewGuid(), "快速应用", Layout: new(40, 80, 360, 300, true));
        current = current with { IsEnabled = enabled, Layout = current.EffectiveLayout with { IsVisible = enabled } };
        _preferences = _preferences with { ShortcutWindows = [current] };
        Synchronize(_preferences);
        Publish();
    }

    private void Wire(AdaptiveDesktopWindow window)
    {
        window.LayoutChanging += Window_LayoutChanging;
        window.LayoutChanged += Window_LayoutChanged;
    }

    private DesktopWidgetLayout Window_LayoutChanging(AdaptiveDesktopWindow window, DesktopWidgetLayout proposed, CollectionWindowLayoutChange change) =>
        _synchronizing ? proposed : NormalizeLayout(window, proposed, change);

    private void Window_LayoutChanged(AdaptiveDesktopWindow window, DesktopWidgetLayout layout)
    {
        if (_synchronizing) return;
        if (window == _shortcut && _preferences.EffectiveShortcutWindow is { } shortcut)
            _preferences = _preferences with { ShortcutWindows = [shortcut with { Layout = layout }] };
        else if (window == _calendar)
            _preferences = _preferences with { Calendar = _preferences.EffectiveCalendar with { Layout = layout } };
        else if (window == _todo)
            _preferences = _preferences with { Todo = _preferences.EffectiveTodo with { Layout = layout } };
        Publish();
    }

    private DesktopWidgetLayout NormalizeLayout(AdaptiveDesktopWindow window, DesktopWidgetLayout layout, CollectionWindowLayoutChange change)
    {
        var area = SystemParameters.WorkArea;
        var workingArea = new LayoutRectangle(area.Left, area.Top, area.Width, area.Height);
        var obstacles = VisibleWindows.Where(candidate => candidate != window)
            .Select(candidate => ToRectangle(candidate.CaptureAdaptiveLayout()))
            .Concat(ExternalObstaclesProvider?.Invoke() ?? [])
            .ToArray();
        var proposed = ToRectangle(layout);
        var resolved = change.Kind is CollectionWindowLayoutChangeKind.MoveCompleted
            ? CollectionWindowLayoutSolver.ResolveMoved(proposed, obstacles, workingArea, minimumWidth: window.AdaptiveMinimumWidth, minimumHeight: window.AdaptiveMinimumHeight)
            : CollectionWindowLayoutSolver.ResolveResized(proposed, obstacles, workingArea, change.ResizeEdge,
                preventOverlap: change.Kind is CollectionWindowLayoutChangeKind.ResizeCompleted,
                minimumWidth: window.AdaptiveMinimumWidth, minimumHeight: window.AdaptiveMinimumHeight);
        return layout with { Left = resolved.Left, Top = resolved.Top, Width = resolved.Width, Height = resolved.Height };
    }

    private void ArrangeVisibleWindows()
    {
        _synchronizing = true;
        try
        {
            foreach (var window in VisibleWindows)
            {
                var normalized = NormalizeLayout(window, window.CaptureAdaptiveLayout(), new(CollectionWindowLayoutChangeKind.MoveCompleted));
                window.ApplyAdaptiveLayout(normalized);
                if (window == _shortcut && _preferences.EffectiveShortcutWindow is { } shortcut)
                    _preferences = _preferences with { ShortcutWindows = [shortcut with { Layout = normalized }] };
                else if (window == _calendar)
                    _preferences = _preferences with { Calendar = _preferences.EffectiveCalendar with { Layout = normalized } };
                else if (window == _todo)
                    _preferences = _preferences with { Todo = _preferences.EffectiveTodo with { Layout = normalized } };
            }
        }
        finally { _synchronizing = false; }
        Publish();
    }

    private void UpdateShortcut(ShortcutWindowDefinition definition)
    {
        if (_synchronizing) return;
        _preferences = _preferences with { ShortcutWindows = [definition] };
        Publish();
    }

    private void UpdateTodo(TodoWindowDefinition definition)
    {
        if (_synchronizing) return;
        _preferences = _preferences with { Todo = definition };
        Publish();
    }

    private void Publish() => PreferencesChanged?.Invoke(_preferences);
    private static LayoutRectangle ToRectangle(DesktopWidgetLayout layout) => new(layout.Left, layout.Top, layout.Width, layout.Height);

    private DesktopWidgetsPreferences CaptureCurrentPreferences()
    {
        var current = _preferences;
        if (_shortcut is not null)
        {
            current = current with { ShortcutWindows = [_shortcut.CaptureDefinition()] };
        }
        if (_calendar is not null)
        {
            current = current with { Calendar = _calendar.CaptureDefinition() };
        }
        if (_todo is not null)
        {
            current = current with { Todo = _todo.CaptureDefinition() };
        }
        return current.Normalize();
    }

    internal void FlushCurrentState()
    {
        if (_disposed)
        {
            return;
        }
        _preferences = CaptureCurrentPreferences();
        Publish();
    }

    private void CloseWindows()
    {
        _shortcut?.Close();
        _calendar?.Close();
        _todo?.Close();
        _shortcut = null;
        _calendar = null;
        _todo = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        FlushCurrentState();
        _disposed = true;
        _synchronizing = true;
        CloseWindows();
    }
}
