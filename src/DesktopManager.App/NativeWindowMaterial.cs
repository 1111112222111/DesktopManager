using System.Runtime.InteropServices;
namespace DesktopManager.App;

internal static class NativeWindowMaterial
{
    private const int DwmwaSystemBackdropType = 38;
    private const uint DwmBbEnable = 0x00000001;

    public static void Apply(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || !OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var backdrop = 1;
            _ = DwmSetWindowAttribute(windowHandle, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
            var blur = new DwmBlurBehind
            {
                Flags = DwmBbEnable,
                Enable = false,
                BlurRegion = IntPtr.Zero,
                TransitionOnMaximized = false
            };
            _ = DwmEnableBlurBehindWindow(windowHandle, ref blur);
        }
        catch (DllNotFoundException)
        {
            // Older Windows versions use the complete WPF material fallback.
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows versions use the complete WPF material fallback.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind blurBehind);

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Enable;
        public IntPtr BlurRegion;
        [MarshalAs(UnmanagedType.Bool)] public bool TransitionOnMaximized;
    }
}
