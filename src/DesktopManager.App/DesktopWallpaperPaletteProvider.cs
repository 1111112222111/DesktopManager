using System.Drawing;
using System.IO;
using DesktopManager.Core;
using Microsoft.Win32;

namespace DesktopManager.App;

internal sealed record DesktopAppearanceSuggestion(
    string SolidColor,
    string GradientStartColor,
    string GradientEndColor,
    string SourceDescription);

internal static class DesktopWallpaperPaletteProvider
{
    private const string DesktopRegistryPath = @"Control Panel\Desktop";

    public static bool TryCreateSuggestion(
        out DesktopAppearanceSuggestion? suggestion,
        out string errorMessage)
    {
        suggestion = null;
        try
        {
            var wallpaperPath = ReadWallpaperPath();
            if (!string.IsNullOrWhiteSpace(wallpaperPath) && File.Exists(wallpaperPath))
            {
                var (primary, secondary) = ReadDominantColors(wallpaperPath!);
                var colors = AdaptiveWindowColors.FromDesktopColors(
                    primary.R, primary.G, primary.B,
                    secondary.R, secondary.G, secondary.B);
                suggestion = new DesktopAppearanceSuggestion(
                    colors.SolidColor,
                    colors.GradientStartColor,
                    colors.GradientEndColor,
                    $"桌面壁纸 · {Path.GetFileName(wallpaperPath)}");
                errorMessage = string.Empty;
                return true;
            }

            if (TryReadSolidDesktopColor(out var desktopColor))
            {
                var colors = AdaptiveWindowColors.FromDesktopColors(
                    desktopColor.R, desktopColor.G, desktopColor.B,
                    desktopColor.R, desktopColor.G, desktopColor.B);
                suggestion = new DesktopAppearanceSuggestion(
                    colors.SolidColor,
                    colors.GradientStartColor,
                    colors.GradientEndColor,
                    "Windows 桌面纯色背景");
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = "未找到当前桌面壁纸或可读取的桌面背景色。";
            return false;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or System.Runtime.InteropServices.ExternalException)
        {
            errorMessage = $"读取桌面颜色失败：{exception.Message}";
            return false;
        }
    }

    private static string? ReadWallpaperPath()
    {
        using var desktopKey = Registry.CurrentUser.OpenSubKey(DesktopRegistryPath);
        var configured = Environment.ExpandEnvironmentVariables(desktopKey?.GetValue("WallPaper")?.ToString() ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var themeCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        return File.Exists(themeCache) ? themeCache : null;
    }

    private static bool TryReadSolidDesktopColor(out Color color)
    {
        using var colorsKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
        var parts = (colorsKey?.GetValue("Background")?.ToString() ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 3
            && byte.TryParse(parts[0], out var red)
            && byte.TryParse(parts[1], out var green)
            && byte.TryParse(parts[2], out var blue))
        {
            color = Color.FromArgb(red, green, blue);
            return true;
        }
        color = default;
        return false;
    }

    private static (Color Primary, Color Secondary) ReadDominantColors(string wallpaperPath)
    {
        using var stream = new FileStream(wallpaperPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
        using var bitmap = new Bitmap(image);
        var stepX = Math.Max(1, bitmap.Width / 80);
        var stepY = Math.Max(1, bitmap.Height / 80);
        var buckets = new Dictionary<int, ColorBucket>();
        for (var y = stepY / 2; y < bitmap.Height; y += stepY)
        {
            for (var x = stepX / 2; x < bitmap.Width; x += stepX)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A < 128)
                {
                    continue;
                }
                var key = ((pixel.R >> 4) << 8) | ((pixel.G >> 4) << 4) | (pixel.B >> 4);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new ColorBucket();
                    buckets.Add(key, bucket);
                }
                bucket.Add(pixel);
            }
        }

        var ranked = buckets.Values
            .OrderByDescending(bucket => bucket.Count)
            .Take(24)
            .Select(bucket => bucket.Average)
            .ToArray();
        if (ranked.Length == 0)
        {
            throw new InvalidDataException("桌面壁纸中没有可分析的颜色。 ");
        }
        var primary = ranked[0];
        var secondary = ranked.Length > 1
            ? ranked.Skip(1).OrderByDescending(color => Distance(primary, color)).First()
            : primary;
        return (primary, secondary);
    }

    private static int Distance(Color first, Color second) =>
        Math.Abs(first.R - second.R) + Math.Abs(first.G - second.G) + Math.Abs(first.B - second.B);

    private sealed class ColorBucket
    {
        private long _red;
        private long _green;
        private long _blue;
        public int Count { get; private set; }
        public Color Average => Color.FromArgb(
            (byte)(_red / Count),
            (byte)(_green / Count),
            (byte)(_blue / Count));

        public void Add(Color color)
        {
            _red += color.R;
            _green += color.G;
            _blue += color.B;
            Count++;
        }
    }
}
