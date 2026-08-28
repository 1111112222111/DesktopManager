using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopManager.Core;
using DesktopManager.Infrastructure;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace DesktopManager.App;

public partial class CollectionWindow : Window, IDisposable
{
    private const string InternalItemDragFormat = "DesktopManager.CollectionItemDrag";
    private readonly CollectionZone _zone;
    private readonly string _zoneDirectory;
    private string _currentDirectory;
    private readonly CollectionZoneStorage _storage;
    private readonly ShellIconProvider _shellIconProvider = new();
    private readonly ObservableCollection<CollectionItemRow> _items = [];
    private readonly Dictionary<string, List<string>> _itemOrders;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _layoutTimer;
    private readonly FileSystemWatcher _watcher;
    private CollectionWindowViewMode _viewMode;
    private string _accentColor = "#AA8959";
    private CollectionWindowAppearance _globalAppearance = CollectionWindowAppearance.Default;
    private CollectionWindowAppearance? _windowAppearanceOverride;
    private CollectionWindowAppearance _appearance = CollectionWindowAppearance.Default;
    private bool _isCollapsed;
    private double _expandedHeight = 300;
    private System.Windows.Point _dragStart;
    private readonly HashSet<CollectionItemRow> _marqueeInitialSelection = [];
    private System.Windows.Point _marqueeStart;
    private bool _isMarqueeSelecting;
    private bool _isMarqueeVisible;
    private HwndSource? _windowSource;
    private double _resizeAspectRatio;
    private double _resizeStartWidthPixels;
    private double _resizeStartHeightPixels;
    private bool? _cornerResizeUsesWidth;
    private bool _interactionWasSized;
    private CollectionWindowResizeEdge _activeResizeEdge;
    private bool _applyingLayout;
    private bool _isDesktopHosted;
    private bool _disposed;

    public event Func<CollectionWindow, CollectionWindowLayout, CollectionWindowLayoutChange, CollectionWindowLayout>? LayoutChanging;
    public event Action<CollectionWindow, CollectionWindowLayout>? LayoutChanged;
    public event Action<CollectionWindow, CollectionWindowItemOrder>? ItemOrderChanged;
    public event Action<string>? OperationCompleted;
    public event Action<CollectionWindowAppearance>? ApplyAppearanceToAllRequested;

    public Guid ZoneId => _zone.Id;
    public string ZoneName => ZoneNameText.Text;
    public string ZoneDirectory => _zoneDirectory;
    public bool IsWindowVisible => IsVisible;
    public bool IsCollapsed => _isCollapsed;
    public bool IsDesktopHosted => _isDesktopHosted;

    public CollectionWindow(
        CollectionZone zone,
        string managedDirectory,
        CollectionWindowLayout layout,
        CollectionWindowAppearance appearance,
        IReadOnlyList<CollectionWindowItemOrder> itemOrders,
        CollectionZoneStorage storage)
    {
        _zone = zone;
        _storage = storage;
        _zoneDirectory = Path.GetFullPath(Path.Combine(managedDirectory, zone.RelativeDirectory));
        _currentDirectory = _zoneDirectory;
        _itemOrders = itemOrders
            .GroupBy(order => NormalizeRelativeDirectory(order.RelativeDirectory), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().EffectiveItemNames.Distinct(StringComparer.CurrentCultureIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(_zoneDirectory);
        InitializeComponent();

        ZoneNameText.Text = string.IsNullOrWhiteSpace(layout.Title) ? zone.Name : layout.Title;
        Left = layout.Left;
        Top = layout.Top;
        Width = Math.Max(MinWidth, layout.Width);
        Height = Math.Max(MinHeight, layout.Height);
        _expandedHeight = Height;
        _viewMode = layout.ViewMode;
        _windowAppearanceOverride = layout.Appearance?.Normalize();
        ApplyAccent(layout.AccentColor);
        ApplyWindowAppearance(appearance);
        ApplyViewMode();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            RefreshItems();
        };
        _layoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _layoutTimer.Tick += (_, _) =>
        {
            _layoutTimer.Stop();
            LayoutChanged?.Invoke(this, CaptureLayout());
        };

        _watcher = new FileSystemWatcher(_zoneDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite
        };
        _watcher.Created += Watcher_Changed;
        _watcher.Deleted += Watcher_Changed;
        _watcher.Changed += Watcher_Changed;
        _watcher.Renamed += Watcher_Changed;
        _watcher.Error += (_, _) => ScheduleRefresh();
        _watcher.EnableRaisingEvents = true;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            _windowSource = HwndSource.FromHwnd(handle);
            _windowSource?.AddHook(WindowMessageHook);
            _isDesktopHosted = DesktopWindowHost.TryAttach(handle, out var desktopHostMessage);
            ApplyEffectiveWindowAppearance();
            StatusText.Text = desktopHostMessage;
            _ = Dispatcher.BeginInvoke(() => OperationCompleted?.Invoke(desktopHostMessage));
        };
        LocationChanged += (_, _) =>
        {
            if (!_applyingLayout)
            {
                ScheduleLayoutSave();
            }
        };
        SizeChanged += (_, _) =>
        {
            if (!_isCollapsed && Height > MinHeight)
            {
                _expandedHeight = Height;
            }
            if (!_applyingLayout)
            {
                ScheduleLayoutSave();
            }
        };
        Closed += (_, _) => Dispose();
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;

        GridItemsList.ItemsSource = _items;
        ListItemsList.ItemsSource = _items;
        RefreshItems();
        if (layout.IsCollapsed)
        {
            SetCollapsed(true);
        }
    }

    public CollectionWindowLayout CaptureLayout() => new(
        _zone.Id,
        Left,
        Top,
        Width,
        _isCollapsed ? _expandedHeight : Height,
        _isCollapsed,
        IsVisible,
        _viewMode,
        _accentColor,
        ZoneNameText.Text,
        _windowAppearanceOverride);

    public void ApplyLayout(CollectionWindowLayout layout)
    {
        _applyingLayout = true;
        try
        {
            Left = layout.Left;
            Top = layout.Top;
            Width = Math.Max(MinWidth, layout.Width);
            if (_isCollapsed)
            {
                _expandedHeight = Math.Max(180, layout.Height);
            }
            else
            {
                Height = Math.Max(180, layout.Height);
                _expandedHeight = Height;
            }
        }
        finally
        {
            _applyingLayout = false;
        }
    }

    public void ApplyAppearance(string title, string accentColor)
    {
        ZoneNameText.Text = string.IsNullOrWhiteSpace(title) ? _zone.Name : title.Trim();
        ApplyAccent(accentColor);
        ScheduleLayoutSave();
    }

    public void ApplyWindowAppearance(CollectionWindowAppearance appearance)
    {
        _globalAppearance = appearance.Normalize();
        ApplyEffectiveWindowAppearance();
    }

    public void ApplyAppearanceAsGlobal(CollectionWindowAppearance appearance)
    {
        _windowAppearanceOverride = null;
        ApplyWindowAppearance(appearance);
    }

    private void ApplyEffectiveWindowAppearance()
    {
        _globalAppearance = _globalAppearance.Normalize();
        if (_windowAppearanceOverride is not null)
        {
            _windowAppearanceOverride = _windowAppearanceOverride.Normalize() with
            {
                SurfaceOpacity = _globalAppearance.SurfaceOpacity,
                AlwaysOnTop = false
            };
        }
        _appearance = CollectionWindowAppearance.Resolve(_globalAppearance, _windowAppearanceOverride);
        Topmost = false;
        if (SystemParameters.HighContrast)
        {
            NativeWindowMaterial.Apply(_windowSource?.Handle ?? IntPtr.Zero);
            return;
        }
        CollectionWindowMaterialRenderer.Apply(
            WindowBorder, _appearance,
            _windowSource?.Handle ?? IntPtr.Zero);
    }

    private void ApplyLayoutCorrection(CollectionWindowLayoutChange change)
    {
        if (!IsLoaded || LayoutChanging is null)
        {
            return;
        }
        var proposed = CaptureLayout();
        var adjusted = LayoutChanging(this, proposed, change);
        if (adjusted != proposed)
        {
            ApplyLayout(adjusted);
        }
    }

    public void RefreshItems()
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            if (!Directory.Exists(_currentDirectory) || !IsWithinZone(_currentDirectory))
            {
                _currentDirectory = _zoneDirectory;
            }
            var rows = _storage.Read(_currentDirectory).Select(ToRow).ToArray();
            var relativeDirectory = GetCurrentRelativeDirectory();
            if (_itemOrders.TryGetValue(relativeDirectory, out var orderedNames))
            {
                rows = CollectionItemOrderResolver.Apply(rows, orderedNames, row => row.Name).ToArray();
            }
            _items.Clear();
            foreach (var row in rows)
            {
                _items.Add(row);
            }
            EmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = rows.Length >= 200 ? "仅显示前 200 项，请打开目录查看全部" : "自动监听目录变化";
            UpdateNavigationBar();
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    public void ApplyVisibility(bool visible)
    {
        if (visible)
        {
            Show();
        }
        else
        {
            Hide();
        }
        LayoutChanged?.Invoke(this, CaptureLayout() with { IsVisible = visible });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ClearItemSelection();
        if (e.ClickCount == 2)
        {
            SetCollapsed(!_isCollapsed);
            return;
        }
        DragMove();
        ScheduleLayoutSave();
    }

    private void WindowSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CollectionWindowSettingsDialog(_appearance, _windowAppearanceOverride is not null)
        {
            Owner = this
        };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        if (dialog.ApplyToAllWindows && dialog.ResultAppearance is not null)
        {
            ApplyAppearanceToAllRequested?.Invoke(dialog.ResultAppearance.Normalize());
            return;
        }
        _windowAppearanceOverride = dialog.UseGlobalAppearance ? null : dialog.ResultAppearance?.Normalize();
        ApplyEffectiveWindowAppearance();
        LayoutChanged?.Invoke(this, CaptureLayout());
    }

    private void ContentPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left || e.ClickCount != 1)
        {
            return;
        }
        if (e.OriginalSource is DependencyObject source
            && (FindAncestor<ListBoxItem>(source) is not null
                || FindAncestor<System.Windows.Controls.Button>(source) is not null
                || FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null))
        {
            return;
        }

        _marqueeStart = e.GetPosition(ContentPanel);
        _isMarqueeSelecting = true;
        _isMarqueeVisible = false;
        _marqueeInitialSelection.Clear();
        var activeList = GetActiveItemsList();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            foreach (var row in activeList.SelectedItems.Cast<CollectionItemRow>())
                _marqueeInitialSelection.Add(row);
        }
        else
        {
            ClearItemSelection();
        }

        SelectionMarquee.Visibility = Visibility.Collapsed;
        Activate();
        ContentPanel.CaptureMouse();
        e.Handled = true;
    }

    private void ContentPanel_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isMarqueeSelecting || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = ClampToContent(e.GetPosition(ContentPanel));
        if (!_isMarqueeVisible
            && Math.Abs(current.X - _marqueeStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _marqueeStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isMarqueeVisible = true;
        var selectionBounds = CreateSelectionBounds(_marqueeStart, current);
        Canvas.SetLeft(SelectionMarquee, selectionBounds.Left);
        Canvas.SetTop(SelectionMarquee, selectionBounds.Top);
        SelectionMarquee.Width = selectionBounds.Width;
        SelectionMarquee.Height = selectionBounds.Height;
        SelectionMarquee.Visibility = Visibility.Visible;
        SelectIntersectingItems(selectionBounds);
        e.Handled = true;
    }

    private void ContentPanel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isMarqueeSelecting || e.ChangedButton is not MouseButton.Left)
        {
            return;
        }

        EndMarqueeSelection(releaseCapture: true);
        GetActiveItemsList().Focus();
        e.Handled = true;
    }

    private void ContentPanel_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) =>
        EndMarqueeSelection(releaseCapture: false);

    private System.Windows.Point ClampToContent(System.Windows.Point point) => new(
        Math.Clamp(point.X, 0, ContentPanel.ActualWidth),
        Math.Clamp(point.Y, 0, ContentPanel.ActualHeight));

    private static Rect CreateSelectionBounds(System.Windows.Point start, System.Windows.Point end) => new(
        new System.Windows.Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y)),
        new System.Windows.Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));

    private void SelectIntersectingItems(Rect selectionBounds)
    {
        var activeList = GetActiveItemsList();
        for (var index = 0; index < activeList.Items.Count; index++)
        {
            if (activeList.Items[index] is not CollectionItemRow row
                || activeList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem item)
            {
                continue;
            }

            var itemBounds = item.TransformToAncestor(ContentPanel)
                .TransformBounds(new Rect(new System.Windows.Point(), item.RenderSize));
            item.IsSelected = _marqueeInitialSelection.Contains(row) || selectionBounds.IntersectsWith(itemBounds);
        }
    }

    private void EndMarqueeSelection(bool releaseCapture)
    {
        _isMarqueeSelecting = false;
        _isMarqueeVisible = false;
        SelectionMarquee.Visibility = Visibility.Collapsed;
        _marqueeInitialSelection.Clear();
        if (releaseCapture && ContentPanel.IsMouseCaptured)
        {
            ContentPanel.ReleaseMouseCapture();
        }
    }

    private void ContentPanel_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && (FindAncestor<ListBoxItem>(source) is not null
                || FindAncestor<System.Windows.Controls.Button>(source) is not null
                || FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null))
        {
            return;
        }

        ClearItemSelection();
        var menu = (System.Windows.Controls.ContextMenu)FindResource("QuickSortContextMenu");
        menu.PlacementTarget = ContentPanel;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void Collapse_Click(object sender, RoutedEventArgs e) => SetCollapsed(!_isCollapsed);

    private void SetCollapsed(bool collapsed)
    {
        _isCollapsed = collapsed;
        ContentPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        ResizeMode = collapsed ? ResizeMode.NoResize : ResizeMode.CanResize;
        if (collapsed)
        {
            _expandedHeight = Math.Max(180, Height);
            Height = 46;
            CollapseIcon.Visibility = Visibility.Collapsed;
            ExpandIcon.Visibility = Visibility.Visible;
            CollapseButton.ToolTip = "展开";
            System.Windows.Automation.AutomationProperties.SetName(CollapseButton, "展开收纳窗口");
        }
        else
        {
            Height = Math.Max(180, _expandedHeight);
            CollapseIcon.Visibility = Visibility.Visible;
            ExpandIcon.Visibility = Visibility.Collapsed;
            CollapseButton.ToolTip = "折叠";
            System.Windows.Automation.AutomationProperties.SetName(CollapseButton, "折叠收纳窗口");
        }
        _ = Dispatcher.BeginInvoke(() => ApplyLayoutCorrection(
            new CollectionWindowLayoutChange(CollectionWindowLayoutChangeKind.MoveCompleted)));
        ScheduleLayoutSave();
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        const int wmNcHitTest = 0x0084;
        const int wmEnterSizeMove = 0x0231;
        const int wmMoving = 0x0216;
        const int wmSizing = 0x0214;
        const int wmExitSizeMove = 0x0232;
        if (message == wmNcHitTest && !_isCollapsed)
        {
            var packed = lParam.ToInt64();
            var screenPoint = new System.Windows.Point(
                unchecked((short)(packed & 0xffff)),
                unchecked((short)((packed >> 16) & 0xffff)));
            var point = PointFromScreen(screenPoint);
            const double edge = 7;
            var left = point.X >= 0 && point.X <= edge;
            var right = point.X <= ActualWidth && point.X >= ActualWidth - edge;
            var top = point.Y >= 0 && point.Y <= edge;
            var bottom = point.Y <= ActualHeight && point.Y >= ActualHeight - edge;
            var hit = (left, right, top, bottom) switch
            {
                (true, _, true, _) => 13,
                (_, true, true, _) => 14,
                (true, _, _, true) => 16,
                (_, true, _, true) => 17,
                (true, _, _, _) => 10,
                (_, true, _, _) => 11,
                (_, _, true, _) => 12,
                (_, _, _, true) => 15,
                _ => 0
            };
            if (hit != 0)
            {
                handled = true;
                return new IntPtr(hit);
            }
        }
        else if (message == wmEnterSizeMove)
        {
            _resizeAspectRatio = ActualHeight > 0 ? ActualWidth / ActualHeight : 1;
            var toDevice = _windowSource?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            _resizeStartWidthPixels = ActualWidth * toDevice.M11;
            _resizeStartHeightPixels = ActualHeight * toDevice.M22;
            _cornerResizeUsesWidth = null;
            _interactionWasSized = false;
            _activeResizeEdge = CollectionWindowResizeEdge.None;
        }
        else if (message == wmMoving)
        {
            // 移动过程中允许覆盖其他收纳窗口，只在松开鼠标后统一求解布局。
        }
        else if (message == wmSizing && wParam.ToInt32() is >= 1 and <= 8)
        {
            _interactionWasSized = true;
            _activeResizeEdge = ToResizeEdge(wParam.ToInt32());
            AdjustSizingRectangle(wParam.ToInt32(), lParam);
            handled = true;
        }
        else if (message == wmExitSizeMove)
        {
            var change = _interactionWasSized
                ? new CollectionWindowLayoutChange(
                    CollectionWindowLayoutChangeKind.ResizeCompleted,
                    _activeResizeEdge)
                : new CollectionWindowLayoutChange(CollectionWindowLayoutChangeKind.MoveCompleted);
            _ = Dispatcher.BeginInvoke(() => ApplyLayoutCorrection(change));
            ScheduleLayoutSave();
        }
        return IntPtr.Zero;
    }

    private void AdjustSizingRectangle(int nativeEdge, IntPtr rectanglePointer)
    {
        if (rectanglePointer == IntPtr.Zero || LayoutChanging is null)
        {
            return;
        }
        var rectangle = Marshal.PtrToStructure<NativeRectangle>(rectanglePointer);
        var resizeEdge = ToResizeEdge(nativeEdge);
        var isCorner = (resizeEdge & (CollectionWindowResizeEdge.Left | CollectionWindowResizeEdge.Right)) != 0
            && (resizeEdge & (CollectionWindowResizeEdge.Top | CollectionWindowResizeEdge.Bottom)) != 0;
        if (isCorner)
        {
            PreserveCornerResizeRatio(ref rectangle);
        }

        var toDevice = _windowSource?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var fromDevice = _windowSource?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new System.Windows.Point(rectangle.Left, rectangle.Top));
        var proposed = CaptureLayout() with
        {
            Left = topLeft.X,
            Top = topLeft.Y,
            Width = Math.Max(1, (rectangle.Right - rectangle.Left) / toDevice.M11),
            Height = Math.Max(1, (rectangle.Bottom - rectangle.Top) / toDevice.M22)
        };
        var snapEdge = resizeEdge;
        if (isCorner)
        {
            snapEdge = _cornerResizeUsesWidth is not false
                ? resizeEdge & (CollectionWindowResizeEdge.Left | CollectionWindowResizeEdge.Right)
                : resizeEdge & (CollectionWindowResizeEdge.Top | CollectionWindowResizeEdge.Bottom);
        }
        var adjusted = LayoutChanging(
            this,
            proposed,
            new CollectionWindowLayoutChange(CollectionWindowLayoutChangeKind.ResizeLive, snapEdge));
        var width = Math.Max(1, (int)Math.Round(adjusted.Width * toDevice.M11));
        var height = Math.Max(1, (int)Math.Round(adjusted.Height * toDevice.M22));
        if (isCorner && _resizeAspectRatio > 0)
        {
            if (_cornerResizeUsesWidth is not false)
            {
                height = Math.Max(1, (int)Math.Round(width / _resizeAspectRatio));
            }
            else
            {
                width = Math.Max(1, (int)Math.Round(height * _resizeAspectRatio));
            }
        }
        ApplyNativeSize(ref rectangle, resizeEdge, width, height);
        Marshal.StructureToPtr(rectangle, rectanglePointer, false);
    }

    private void PreserveCornerResizeRatio(ref NativeRectangle rectangle)
    {
        if (_resizeAspectRatio <= 0)
        {
            return;
        }
        var width = Math.Max(1, rectangle.Right - rectangle.Left);
        var height = Math.Max(1, rectangle.Bottom - rectangle.Top);
        if (_cornerResizeUsesWidth is null)
        {
            var relativeWidthChange = Math.Abs(width - _resizeStartWidthPixels)
                / Math.Max(1, _resizeStartWidthPixels);
            var relativeHeightChange = Math.Abs(height - _resizeStartHeightPixels)
                / Math.Max(1, _resizeStartHeightPixels);
            if (relativeWidthChange > 0.003 || relativeHeightChange > 0.003)
            {
                _cornerResizeUsesWidth = relativeWidthChange >= relativeHeightChange;
            }
        }
        if (_cornerResizeUsesWidth is not false)
        {
            height = Math.Max(1, (int)Math.Round(width / _resizeAspectRatio));
        }
        else
        {
            width = Math.Max(1, (int)Math.Round(height * _resizeAspectRatio));
        }
        ApplyNativeSize(ref rectangle, _activeResizeEdge, width, height);
    }

    private static void ApplyNativeSize(
        ref NativeRectangle rectangle,
        CollectionWindowResizeEdge edge,
        int width,
        int height)
    {
        if ((edge & CollectionWindowResizeEdge.Left) != 0)
        {
            rectangle.Left = rectangle.Right - width;
        }
        else if ((edge & CollectionWindowResizeEdge.Right) != 0)
        {
            rectangle.Right = rectangle.Left + width;
        }
        if ((edge & CollectionWindowResizeEdge.Top) != 0)
        {
            rectangle.Top = rectangle.Bottom - height;
        }
        else if ((edge & CollectionWindowResizeEdge.Bottom) != 0)
        {
            rectangle.Bottom = rectangle.Top + height;
        }
    }

    private static CollectionWindowResizeEdge ToResizeEdge(int nativeEdge) => nativeEdge switch
    {
        1 => CollectionWindowResizeEdge.Left,
        2 => CollectionWindowResizeEdge.Right,
        3 => CollectionWindowResizeEdge.Top,
        4 => CollectionWindowResizeEdge.Left | CollectionWindowResizeEdge.Top,
        5 => CollectionWindowResizeEdge.Right | CollectionWindowResizeEdge.Top,
        6 => CollectionWindowResizeEdge.Bottom,
        7 => CollectionWindowResizeEdge.Left | CollectionWindowResizeEdge.Bottom,
        8 => CollectionWindowResizeEdge.Right | CollectionWindowResizeEdge.Bottom,
        _ => CollectionWindowResizeEdge.None
    };

    private void ViewMode_Click(object sender, RoutedEventArgs e)
    {
        _viewMode = _viewMode is CollectionWindowViewMode.Grid
            ? CollectionWindowViewMode.List
            : CollectionWindowViewMode.Grid;
        ApplyViewMode();
        ScheduleLayoutSave();
    }

    private void ApplyViewMode()
    {
        GridItemsList.Visibility = _viewMode is CollectionWindowViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        ListItemsList.Visibility = _viewMode is CollectionWindowViewMode.List ? Visibility.Visible : Visibility.Collapsed;
        ListModeIcon.Visibility = _viewMode is CollectionWindowViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        GridModeIcon.Visibility = _viewMode is CollectionWindowViewMode.List ? Visibility.Visible : Visibility.Collapsed;
        ViewModeButton.ToolTip = _viewMode is CollectionWindowViewMode.Grid ? "切换为列表视图" : "切换为网格视图";
        System.Windows.Automation.AutomationProperties.SetName(
            ViewModeButton,
            _viewMode is CollectionWindowViewMode.Grid ? "切换为列表视图" : "切换为网格视图");
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Move
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(InternalItemDragFormat)
            && e.Data.GetData(InternalItemDragFormat) is string draggedPath
            && string.Equals(Path.GetDirectoryName(draggedPath), _currentDirectory, StringComparison.OrdinalIgnoreCase))
        {
            ReorderDroppedItem(draggedPath, e.OriginalSource as DependencyObject);
            e.Handled = true;
            return;
        }
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }
        var results = await Task.Run(() => _storage.MoveInto(_currentDirectory, paths));
        var succeeded = results.Count(result => result.Succeeded);
        var failed = results.Where(result => !result.Succeeded).ToArray();
        RefreshItems();
        StatusText.Text = failed.Length == 0
            ? $"已直接收纳 {succeeded} 项"
            : $"已收纳 {succeeded} 项，失败 {failed.Length} 项：{failed[0].Error}";
        OperationCompleted?.Invoke(StatusText.Text);
    }

    private void Items_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton is not MouseButtonState.Pressed)
        {
            _dragStart = e.GetPosition(this);
            return;
        }
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }
        if (GetSelectedItem() is not { } selected)
        {
            return;
        }
        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.FileDrop, new[] { selected.Path });
        data.SetData(
            InternalItemDragFormat,
            selected.Path);
        _ = System.Windows.DragDrop.DoDragDrop(
            (DependencyObject)sender,
            data,
            System.Windows.DragDropEffects.Move);
        ScheduleRefresh();
    }

    private void Items_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _dragStart = e.GetPosition(this);

    private void Items_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not ListBoxItem)
        {
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        if (element is ListBoxItem item)
        {
            item.IsSelected = true;
        }
    }

    private void Items_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox list && list.SelectedItem is null)
        {
            e.Handled = true;
        }
    }

    private void Items_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedItem();
    private void OpenItem_Click(object sender, RoutedEventArgs e) => OpenSelectedItem();

    private void Items_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key is Key.Up)
        {
            MoveSelectedItem(-1);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key is Key.Down)
        {
            MoveSelectedItem(1);
            e.Handled = true;
        }
        else if (e.Key is Key.Enter)
        {
            OpenSelectedItem();
            e.Handled = true;
        }
        else if (e.Key is Key.F2)
        {
            RenameItem_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key is Key.Delete)
        {
            DeleteItem_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key is Key.Back && NavigateUp())
        {
            e.Handled = true;
        }
    }

    private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }
        if (e.Key is Key.C)
        {
            CopySelectedItemToClipboard();
            e.Handled = true;
        }
        else if (e.Key is Key.V)
        {
            e.Handled = true;
            await PasteClipboardAsync();
        }
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
        {
            return;
        }
        var list = GetActiveItemsList();
        var index = CollectionItemTypeAhead.FindNextIndex(
            _items.Select(item => item.Name).ToArray(),
            list.SelectedIndex,
            e.Text);
        if (index < 0)
        {
            return;
        }
        list.SelectedIndex = index;
        list.ScrollIntoView(list.SelectedItem);
        list.Focus();
        e.Handled = true;
    }

    private void OpenSelectedItem()
    {
        if (GetSelectedItem() is not { } item)
        {
            return;
        }
        try
        {
            if (item.Kind is DesktopItemKind.Folder && TryNavigateInto(item.Path))
            {
                return;
            }
            Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void CopyItem_Click(object sender, RoutedEventArgs e) => CopySelectedItemToClipboard();

    private async void PasteItem_Click(object sender, RoutedEventArgs e) => await PasteClipboardAsync();

    private void CopySelectedItemToClipboard()
    {
        if (GetSelectedItem() is not { } item)
        {
            return;
        }
        try
        {
            var paths = new StringCollection { item.Path };
            System.Windows.Clipboard.SetFileDropList(paths);
            StatusText.Text = $"已复制“{item.Name}”到剪贴板";
            OperationCompleted?.Invoke(StatusText.Text);
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            ShowError($"无法访问 Windows 剪贴板：{exception.Message}");
        }
    }

    private async Task PasteClipboardAsync()
    {
        string[] paths;
        try
        {
            if (!System.Windows.Clipboard.ContainsFileDropList())
            {
                StatusText.Text = "剪贴板中没有可粘贴的文件或文件夹";
                OperationCompleted?.Invoke(StatusText.Text);
                return;
            }
            paths = System.Windows.Clipboard.GetFileDropList()
                .Cast<string>()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            ShowError($"无法读取 Windows 剪贴板：{exception.Message}");
            return;
        }
        if (paths.Length == 0)
        {
            return;
        }

        var results = await Task.Run(() => _storage.CopyInto(_currentDirectory, paths));
        var succeeded = results.Count(result => result.Succeeded);
        var failed = results.Where(result => !result.Succeeded).ToArray();
        RefreshItems();
        StatusText.Text = failed.Length == 0
            ? $"已粘贴 {succeeded} 项"
            : $"已粘贴 {succeeded} 项，失败 {failed.Length} 项：{failed[0].Error}";
        OperationCompleted?.Invoke(StatusText.Text);
    }

    private void MoveItemUp_Click(object sender, RoutedEventArgs e) => MoveSelectedItem(-1);

    private void MoveItemDown_Click(object sender, RoutedEventArgs e) => MoveSelectedItem(1);

    private void SortByName_Click(object sender, RoutedEventArgs e) =>
        ApplyQuickSort(CollectionItemSortMode.Name, "已按首字母排序");

    private void SortBySize_Click(object sender, RoutedEventArgs e) =>
        ApplyQuickSort(CollectionItemSortMode.Size, "已按文件大小排序");

    private void SortByKind_Click(object sender, RoutedEventArgs e) =>
        ApplyQuickSort(CollectionItemSortMode.Kind, "已按类别排序");

    private void SortByModifiedAt_Click(object sender, RoutedEventArgs e) =>
        ApplyQuickSort(CollectionItemSortMode.ModifiedAt, "已按修改日期排序");

    private void ApplyQuickSort(CollectionItemSortMode mode, string message)
    {
        var sorted = CollectionItemQuickSorter.Apply(
            _items.ToArray(),
            mode,
            item => item.Name,
            item => item.Kind,
            item => item.Size,
            item => item.ModifiedAt);
        _items.Clear();
        foreach (var item in sorted)
        {
            _items.Add(item);
        }
        PersistCurrentItemOrder(message);
    }

    private void MoveSelectedItem(int offset)
    {
        var selected = GetSelectedItem();
        if (selected is null)
        {
            return;
        }
        var sourceIndex = _items.IndexOf(selected);
        var targetIndex = Math.Clamp(sourceIndex + offset, 0, _items.Count - 1);
        if (sourceIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }
        _items.Move(sourceIndex, targetIndex);
        CompleteItemReorder(targetIndex);
    }

    private void ReorderDroppedItem(string path, DependencyObject? dropSource)
    {
        var sourceIndex = _items.ToList().FindIndex(item =>
            string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0)
        {
            return;
        }
        var targetContainer = dropSource is null ? null : FindAncestor<ListBoxItem>(dropSource);
        var targetIndex = targetContainer?.DataContext is CollectionItemRow target
            ? _items.IndexOf(target)
            : _items.Count - 1;
        if (targetIndex < 0 || targetIndex == sourceIndex)
        {
            return;
        }
        _items.Move(sourceIndex, targetIndex);
        CompleteItemReorder(targetIndex);
    }

    private void CompleteItemReorder(int selectedIndex)
    {
        var activeList = GetActiveItemsList();
        activeList.SelectedIndex = selectedIndex;
        activeList.ScrollIntoView(activeList.SelectedItem);
        PersistCurrentItemOrder("已保存窗口内项目顺序");
    }

    private void PersistCurrentItemOrder(string message)
    {
        var relativeDirectory = GetCurrentRelativeDirectory();
        var names = _items.Select(item => item.Name).ToList();
        _itemOrders[relativeDirectory] = names;
        ItemOrderChanged?.Invoke(
            this,
            new CollectionWindowItemOrder(ZoneId, relativeDirectory, names.ToArray()));
        StatusText.Text = message;
        OperationCompleted?.Invoke(StatusText.Text);
    }

    private void LocateItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedItem() is not { } item)
        {
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true });
    }

    private async void RenameItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedItem() is not { } item)
        {
            return;
        }
        var dialog = new CollectionRenameWindow(item.Name) { Owner = this };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }
        await RunOperationAsync(() => _storage.Rename(_zoneDirectory, item.Path, dialog.NewName), "已重命名项目");
    }

    private async void MoveOutItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedItem() is not { } item)
        {
            return;
        }
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择移出目标目录",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() is not Forms.DialogResult.OK)
        {
            return;
        }
        await RunOperationAsync(() => _storage.MoveOut(_zoneDirectory, item.Path, dialog.SelectedPath), "已移出项目");
    }

    private async void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedItem() is not { } item)
        {
            return;
        }
        await RunOperationAsync(() => _storage.DeleteToRecycleBin(_zoneDirectory, item.Path), "已移入回收站");
    }

    private async Task RunOperationAsync(Func<CollectionFileOperationResult> operation, string successMessage)
    {
        try
        {
            await Task.Run(operation);
            RefreshItems();
            StatusText.Text = successMessage;
            OperationCompleted?.Invoke(successMessage);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_currentDirectory) { UseShellExecute = true });

    private void NavigateUp_Click(object sender, RoutedEventArgs e) => NavigateUp();

    private bool TryNavigateInto(string directory)
    {
        var target = Path.GetFullPath(directory);
        if (!Directory.Exists(target) || !IsWithinZone(target))
        {
            return false;
        }
        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }
        _currentDirectory = target;
        RefreshItems();
        GetActiveItemsList().Focus();
        return true;
    }

    private bool NavigateUp()
    {
        if (string.Equals(_currentDirectory, _zoneDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var parent = Directory.GetParent(_currentDirectory)?.FullName;
        if (parent is null || !IsWithinZone(parent))
        {
            _currentDirectory = _zoneDirectory;
        }
        else
        {
            _currentDirectory = parent;
        }
        RefreshItems();
        GetActiveItemsList().Focus();
        return true;
    }

    private bool IsWithinZone(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, _zoneDirectory, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(_zoneDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private string GetCurrentRelativeDirectory() => NormalizeRelativeDirectory(
        Path.GetRelativePath(_zoneDirectory, _currentDirectory));

    private static string NormalizeRelativeDirectory(string relativeDirectory) =>
        relativeDirectory is "." or ""
            ? ""
            : relativeDirectory
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Trim(Path.DirectorySeparatorChar);

    private void UpdateNavigationBar()
    {
        var atRoot = string.Equals(_currentDirectory, _zoneDirectory, StringComparison.OrdinalIgnoreCase);
        NavigationBar.Visibility = atRoot ? Visibility.Collapsed : Visibility.Visible;
        if (!atRoot)
        {
            var relative = Path.GetRelativePath(_zoneDirectory, _currentDirectory);
            CurrentPathText.Text = $"{ZoneNameText.Text}  /  {relative.Replace(Path.DirectorySeparatorChar.ToString(), " / ")}";
        }
    }

    private CollectionItemRow? GetSelectedItem() =>
        (GridItemsList.Visibility is Visibility.Visible ? GridItemsList.SelectedItem : ListItemsList.SelectedItem)
        as CollectionItemRow;

    private void ClearItemSelection()
    {
        GridItemsList.SelectedIndex = -1;
        ListItemsList.SelectedIndex = -1;
    }

    private System.Windows.Controls.ListBox GetActiveItemsList() =>
        GridItemsList.Visibility is Visibility.Visible ? GridItemsList : ListItemsList;

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }
        return null;
    }

    private void Watcher_Changed(object sender, FileSystemEventArgs e) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    private void ScheduleLayoutSave()
    {
        if (!IsLoaded)
        {
            return;
        }
        _layoutTimer.Stop();
        _layoutTimer.Start();
    }

    private void ApplyAccent(string value)
    {
        // “静奢玻璃”只保留一条香槟金归档脊线，旧版本保存的彩色标题不再影响视觉语言。
        _accentColor = "#AA8959";
        if (SystemParameters.HighContrast)
        {
            WindowBorder.Background = System.Windows.SystemColors.WindowBrush;
            WindowBorder.BorderBrush = System.Windows.SystemColors.WindowTextBrush;
            TitleBar.Background = System.Windows.SystemColors.HighlightBrush;
            ZoneNameText.Foreground = System.Windows.SystemColors.HighlightTextBrush;
            return;
        }
        WindowBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("GlassLineBrush");
        TitleBar.Background = System.Windows.Media.Brushes.Transparent;
        ZoneNameText.Foreground = (System.Windows.Media.Brush)FindResource("GlassTextBrush");
        ApplyEffectiveWindowAppearance();
    }

    private void SystemParameters_StaticPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.HighContrast))
        {
            ApplyAccent(_accentColor);
        }
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        MessageBox.Show(this, message, "收纳窗口", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private CollectionItemRow ToRow(CollectionWindowItem item) => new(
        item.Path,
        item.Name,
        item.Kind,
        item.Size,
        item.ModifiedAt,
        _shellIconProvider.GetIcon(item.Path),
        item.Kind is DesktopItemKind.Folder ? "—" : FormatSize(item.Size));

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824d:0.#} GB",
        >= 1_048_576 => $"{bytes / 1_048_576d:0.#} MB",
        >= 1_024 => $"{bytes / 1_024d:0.#} KB",
        _ => $"{bytes} B"
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        _refreshTimer.Stop();
        _layoutTimer.Stop();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed record CollectionItemRow(
        string Path,
        string Name,
        DesktopItemKind Kind,
        long Size,
        DateTimeOffset ModifiedAt,
        ImageSource Icon,
        string SizeText);

}
