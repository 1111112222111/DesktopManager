using System.Windows.Controls;
using System.Windows.Media;
using DesktopManager.Core;
using Media = System.Windows.Media;
using WpfPoint = System.Windows.Point;

namespace DesktopManager.App;

internal static class CollectionWindowMaterialRenderer
{
    public static void Apply(
        Border surface,
        CollectionWindowAppearance appearance,
        IntPtr windowHandle = default)
    {
        var normalized = appearance.Normalize();
        var start = ParseColor(normalized.SurfaceColor);
        surface.Background = normalized.FillMode is CollectionWindowFillMode.Gradient
            ? new LinearGradientBrush(
                WithAlpha(start, normalized.SurfaceOpacity),
                WithAlpha(ParseColor(normalized.GradientEndColor), normalized.SurfaceOpacity),
                new WpfPoint(0, 0),
                new WpfPoint(0, 1))
            : new SolidColorBrush(WithAlpha(start, normalized.SurfaceOpacity));
        NativeWindowMaterial.Apply(windowHandle);
    }

    private static Media.Color ParseColor(string value) =>
        (Media.Color)Media.ColorConverter.ConvertFromString(value);

    private static Media.Color WithAlpha(Media.Color color, double opacity) =>
        Media.Color.FromArgb(
            (byte)Math.Clamp(Math.Round(opacity * byte.MaxValue), 0, byte.MaxValue),
            color.R,
            color.G,
            color.B);
}
