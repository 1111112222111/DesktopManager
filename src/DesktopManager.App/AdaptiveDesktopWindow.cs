using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopManager.Core;

namespace DesktopManager.App;

/// <summary>为非文件型桌面组件提供与收纳窗口一致的拖动完成吸附和边框缩放语义。</summary>
public abstract class AdaptiveDesktopWindow : Window
{
    private HwndSource? _source;
    private double _aspectRatio;
    private double _startWidthPixels;
    private double _startHeightPixels;
    private bool? _cornerUsesWidth;
    private bool _interactionWasSized;
    private CollectionWindowResizeEdge _activeResizeEdge;
    private bool _applyingLayout;
    private bool _initialized;

    internal event Func<AdaptiveDesktopWindow, DesktopWidgetLayout, CollectionWindowLayoutChange, DesktopWidgetLayout>? LayoutChanging;
    internal event Action<AdaptiveDesktopWindow, DesktopWidgetLayout>? LayoutChanged;

    internal bool IsDesktopHosted { get; private set; }
    internal abstract string LayoutKey { get; }
    internal double AdaptiveMinimumWidth { get; private set; } = 280;
    internal double AdaptiveMinimumHeight { get; private set; } = 180;

    protected AdaptiveDesktopWindow()
    {
        SourceInitialized += Window_SourceInitialized;
        Closed += (_, _) => _source?.RemoveHook(WindowMessageHook);
    }

    protected void InitializeAdaptiveLayout(DesktopWidgetLayout layout, double minimumWidth, double minimumHeight)
    {
        AdaptiveMinimumWidth = minimumWidth;
        AdaptiveMinimumHeight = minimumHeight;
        MinWidth = minimumWidth;
        MinHeight = minimumHeight;
        ApplyAdaptiveLayout(layout);
        _initialized = true;
    }

    internal DesktopWidgetLayout CaptureAdaptiveLayout() =>
        new(Left, Top, Width, Height, IsVisible);

    internal void ApplyAdaptiveLayout(DesktopWidgetLayout layout)
    {
        _applyingLayout = true;
        try
        {
            Left = layout.Left;
            Top = layout.Top;
            Width = Math.Max(AdaptiveMinimumWidth, layout.Width);
            Height = Math.Max(AdaptiveMinimumHeight, layout.Height);
        }
        finally
        {
            _applyingLayout = false;
        }
    }

    protected void BeginAdaptiveDrag()
    {
        DragMove();
    }

    internal void ApplyVisibility(bool visible)
    {
        if (visible) Show(); else Hide();
        LayoutChanged?.Invoke(this, CaptureAdaptiveLayout() with { IsVisible = visible });
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessageHook);
        IsDesktopHosted = DesktopWindowHost.TryAttach(handle, out _);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmNcHitTest = 0x0084;
        const int wmEnterSizeMove = 0x0231;
        const int wmSizing = 0x0214;
        const int wmExitSizeMove = 0x0232;
        if (message == wmNcHitTest)
        {
            var packed = lParam.ToInt64();
            var point = PointFromScreen(new System.Windows.Point(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff))));
            const double edge = 7;
            var left = point.X >= 0 && point.X <= edge;
            var right = point.X <= ActualWidth && point.X >= ActualWidth - edge;
            var top = point.Y >= 0 && point.Y <= edge;
            var bottom = point.Y <= ActualHeight && point.Y >= ActualHeight - edge;
            var hit = (left, right, top, bottom) switch
            {
                (true, _, true, _) => 13, (_, true, true, _) => 14,
                (true, _, _, true) => 16, (_, true, _, true) => 17,
                (true, _, _, _) => 10, (_, true, _, _) => 11,
                (_, _, true, _) => 12, (_, _, _, true) => 15, _ => 0
            };
            if (hit != 0) { handled = true; return new IntPtr(hit); }
        }
        else if (message == wmEnterSizeMove)
        {
            _aspectRatio = ActualHeight > 0 ? ActualWidth / ActualHeight : 1;
            var toDevice = _source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            _startWidthPixels = ActualWidth * toDevice.M11;
            _startHeightPixels = ActualHeight * toDevice.M22;
            _cornerUsesWidth = null;
            _interactionWasSized = false;
            _activeResizeEdge = CollectionWindowResizeEdge.None;
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
                ? new CollectionWindowLayoutChange(CollectionWindowLayoutChangeKind.ResizeCompleted, _activeResizeEdge)
                : new CollectionWindowLayoutChange(CollectionWindowLayoutChangeKind.MoveCompleted);
            _ = Dispatcher.BeginInvoke(() => ApplyLayoutCorrection(change));
        }
        return IntPtr.Zero;
    }

    private void ApplyLayoutCorrection(CollectionWindowLayoutChange change)
    {
        if (!_initialized || LayoutChanging is null) { PublishLayout(); return; }
        var proposed = CaptureAdaptiveLayout();
        var adjusted = LayoutChanging(this, proposed, change);
        if (adjusted != proposed) ApplyAdaptiveLayout(adjusted);
        PublishLayout();
    }

    private void PublishLayout()
    {
        if (_initialized && !_applyingLayout) LayoutChanged?.Invoke(this, CaptureAdaptiveLayout());
    }

    private void AdjustSizingRectangle(int nativeEdge, IntPtr pointer)
    {
        if (pointer == IntPtr.Zero || LayoutChanging is null) return;
        var rectangle = Marshal.PtrToStructure<NativeRectangle>(pointer);
        var resizeEdge = ToResizeEdge(nativeEdge);
        var isCorner = (resizeEdge & (CollectionWindowResizeEdge.Left | CollectionWindowResizeEdge.Right)) != 0
            && (resizeEdge & (CollectionWindowResizeEdge.Top | CollectionWindowResizeEdge.Bottom)) != 0;
        if (isCorner) PreserveCornerRatio(ref rectangle, resizeEdge);
        var toDevice = _source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var fromDevice = _source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new System.Windows.Point(rectangle.Left, rectangle.Top));
        var proposed = CaptureAdaptiveLayout() with
        {
            Left = topLeft.X, Top = topLeft.Y,
            Width = Math.Max(1, (rectangle.Right - rectangle.Left) / toDevice.M11),
            Height = Math.Max(1, (rectangle.Bottom - rectangle.Top) / toDevice.M22)
        };
        var snapEdge = isCorner
            ? (_cornerUsesWidth is not false ? resizeEdge & (CollectionWindowResizeEdge.Left | CollectionWindowResizeEdge.Right) : resizeEdge & (CollectionWindowResizeEdge.Top | CollectionWindowResizeEdge.Bottom))
            : resizeEdge;
        var adjusted = LayoutChanging(this, proposed, new CollectionWindowLayoutChange(CollectionWindowLayoutChangeKind.ResizeLive, snapEdge));
        var width = Math.Max(1, (int)Math.Round(adjusted.Width * toDevice.M11));
        var height = Math.Max(1, (int)Math.Round(adjusted.Height * toDevice.M22));
        if (isCorner && _aspectRatio > 0)
        {
            if (_cornerUsesWidth is not false) height = Math.Max(1, (int)Math.Round(width / _aspectRatio));
            else width = Math.Max(1, (int)Math.Round(height * _aspectRatio));
        }
        ApplyNativeSize(ref rectangle, resizeEdge, width, height);
        Marshal.StructureToPtr(rectangle, pointer, false);
    }

    private void PreserveCornerRatio(ref NativeRectangle rectangle, CollectionWindowResizeEdge edge)
    {
        if (_aspectRatio <= 0) return;
        var width = Math.Max(1, rectangle.Right - rectangle.Left);
        var height = Math.Max(1, rectangle.Bottom - rectangle.Top);
        if (_cornerUsesWidth is null)
        {
            var widthChange = Math.Abs(width - _startWidthPixels) / Math.Max(1, _startWidthPixels);
            var heightChange = Math.Abs(height - _startHeightPixels) / Math.Max(1, _startHeightPixels);
            if (widthChange > 0.003 || heightChange > 0.003) _cornerUsesWidth = widthChange >= heightChange;
        }
        if (_cornerUsesWidth is not false) height = Math.Max(1, (int)Math.Round(width / _aspectRatio));
        else width = Math.Max(1, (int)Math.Round(height * _aspectRatio));
        ApplyNativeSize(ref rectangle, edge, width, height);
    }

    private static void ApplyNativeSize(ref NativeRectangle rectangle, CollectionWindowResizeEdge edge, int width, int height)
    {
        if ((edge & CollectionWindowResizeEdge.Left) != 0) rectangle.Left = rectangle.Right - width;
        else if ((edge & CollectionWindowResizeEdge.Right) != 0) rectangle.Right = rectangle.Left + width;
        if ((edge & CollectionWindowResizeEdge.Top) != 0) rectangle.Top = rectangle.Bottom - height;
        else if ((edge & CollectionWindowResizeEdge.Bottom) != 0) rectangle.Bottom = rectangle.Top + height;
    }

    private static CollectionWindowResizeEdge ToResizeEdge(int edge) => edge switch
    {
        1 => CollectionWindowResizeEdge.Left, 2 => CollectionWindowResizeEdge.Right,
        3 => CollectionWindowResizeEdge.Top, 4 => CollectionWindowResizeEdge.Left | CollectionWindowResizeEdge.Top,
        5 => CollectionWindowResizeEdge.Right | CollectionWindowResizeEdge.Top, 6 => CollectionWindowResizeEdge.Bottom,
        7 => CollectionWindowResizeEdge.Left | CollectionWindowResizeEdge.Bottom,
        8 => CollectionWindowResizeEdge.Right | CollectionWindowResizeEdge.Bottom, _ => CollectionWindowResizeEdge.None
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle { public int Left; public int Top; public int Right; public int Bottom; }
}
