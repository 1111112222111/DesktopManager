using System.Runtime.InteropServices;

namespace DesktopManager.App;

/// <summary>
/// 将收纳窗口挂到 Windows 桌面图标宿主层，使其跟随桌面而不是参与普通应用窗口切换。
/// </summary>
internal static class DesktopWindowHost
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = 0x80000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint SmtoNormal = 0x0000;
    private static readonly nint HwndTop = nint.Zero;

    public static bool TryAttach(nint windowHandle, out string message)
    {
        if (windowHandle == nint.Zero)
        {
            message = "收纳窗口尚未创建。";
            return false;
        }
        var desktopHost = FindDesktopHost();
        if (desktopHost == nint.Zero)
        {
            message = "未找到 Windows 桌面宿主层，收纳窗口暂时保持普通桌面工具窗口。";
            return false;
        }

        var rectangle = new NativeRectangle();
        if (!GetWindowRect(windowHandle, ref rectangle))
        {
            message = "无法读取收纳窗口位置。";
            return false;
        }
        var style = GetWindowLongPointer(windowHandle, GwlStyle).ToInt64();
        var extendedStyle = GetWindowLongPointer(windowHandle, GwlExStyle).ToInt64();
        SetWindowLongPointer(windowHandle, GwlStyle, new nint((style & ~WsPopup) | WsChild));
        SetWindowLongPointer(
            windowHandle,
            GwlExStyle,
            new nint((extendedStyle | WsExToolWindow) & ~WsExAppWindow));

        Marshal.SetLastPInvokeError(0);
        var previousParent = SetParent(windowHandle, desktopHost);
        if (previousParent == nint.Zero && Marshal.GetLastPInvokeError() != 0)
        {
            var primaryError = Marshal.GetLastPInvokeError();
            var rootDesktop = GetDesktopWindow();
            if (desktopHost == rootDesktop)
            {
                SetWindowLongPointer(windowHandle, GwlStyle, new nint(style));
                SetWindowLongPointer(windowHandle, GwlExStyle, new nint(extendedStyle));
                message = $"无法挂接 Windows 桌面宿主层（错误 {primaryError}）。";
                return false;
            }
            Marshal.SetLastPInvokeError(0);
            previousParent = SetParent(windowHandle, rootDesktop);
            if (previousParent == nint.Zero && Marshal.GetLastPInvokeError() != 0)
            {
                var fallbackError = Marshal.GetLastPInvokeError();
                SetWindowLongPointer(windowHandle, GwlStyle, new nint(style));
                SetWindowLongPointer(windowHandle, GwlExStyle, new nint(extendedStyle));
                message = $"无法挂接 Windows 桌面宿主层（错误 {primaryError}/{fallbackError}）。";
                return false;
            }
            desktopHost = rootDesktop;
        }
        var origin = new NativePoint(rectangle.Left, rectangle.Top);
        _ = MapWindowPoints(nint.Zero, desktopHost, ref origin, 1);
        var width = Math.Max(1, rectangle.Right - rectangle.Left);
        var height = Math.Max(1, rectangle.Bottom - rectangle.Top);
        if (!SetWindowPos(
                windowHandle,
                HwndTop,
                origin.X,
                origin.Y,
                width,
                height,
                SwpNoActivate | SwpFrameChanged | SwpShowWindow))
        {
            message = $"收纳窗口已挂接桌面，但位置同步失败（错误 {Marshal.GetLastPInvokeError()}）。";
            return true;
        }
        message = "收纳窗口已固定到 Windows 桌面。";
        return true;
    }

    private static nint FindDesktopHost()
    {
        var programManager = FindWindow("Progman", null);
        if (programManager != nint.Zero)
        {
            _ = SendMessageTimeout(
                programManager,
                0x052C,
                nint.Zero,
                nint.Zero,
                SmtoNormal,
                1000,
                out _);
        }

        nint host = nint.Zero;
        _ = EnumWindows((candidate, _) =>
        {
            if (FindWindowEx(candidate, nint.Zero, "SHELLDLL_DefView", null) == nint.Zero)
            {
                return true;
            }
            host = candidate;
            return false;
        }, nint.Zero);
        if (host != nint.Zero)
        {
            return host;
        }
        if (programManager != nint.Zero)
        {
            return programManager;
        }
        return GetDesktopWindow();
    }

    private static nint GetWindowLongPointer(nint window, int index) => IntPtr.Size == 8
        ? GetWindowLongPtr64(window, index)
        : new nint(GetWindowLong32(window, index));

    private static void SetWindowLongPointer(nint window, int index, nint value)
    {
        if (IntPtr.Size == 8)
        {
            _ = SetWindowLongPtr64(window, index, value);
        }
        else
        {
            _ = SetWindowLong32(window, index, value.ToInt32());
        }
    }

    private delegate bool EnumWindowsProcedure(nint window, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProcedure callback, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint newParent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, ref NativeRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int MapWindowPoints(nint from, nint to, ref NativePoint point, uint pointCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong32(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong32(nint window, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        uint flags,
        uint timeout,
        out nint result);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
