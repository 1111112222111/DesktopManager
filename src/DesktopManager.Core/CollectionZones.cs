using System.Security.Cryptography;
using System.Text;

namespace DesktopManager.Core;

public enum CollectionWindowViewMode
{
    Grid,
    List
}

public enum CollectionWindowFillMode
{
    Solid = 0,
    Gradient = 1
}

public sealed record CollectionZone(
    Guid Id,
    string Name,
    string RelativeDirectory,
    IReadOnlyList<Guid> RuleIds,
    bool HasEnabledRule);

public sealed record CollectionWindowLayout(
    Guid ZoneId,
    double Left,
    double Top,
    double Width = 360,
    double Height = 300,
    bool IsCollapsed = false,
    bool IsVisible = true,
    CollectionWindowViewMode ViewMode = CollectionWindowViewMode.Grid,
    string AccentColor = "#AA8959",
    string? Title = null,
    CollectionWindowAppearance? Appearance = null);

public sealed record CollectionWindowAppearance(
    double SurfaceOpacity = 0.86,
    string SurfaceColor = "#232B28",
    bool AlwaysOnTop = false,
    CollectionWindowFillMode FillMode = CollectionWindowFillMode.Solid,
    string GradientEndColor = "#151B19")
{
    public static CollectionWindowAppearance Default { get; } = new();

    public CollectionWindowAppearance Normalize() => new(
        double.IsFinite(SurfaceOpacity) ? Math.Clamp(SurfaceOpacity, 0.18, 0.96) : Default.SurfaceOpacity,
        IsRgbHex(SurfaceColor) ? SurfaceColor.ToUpperInvariant() : Default.SurfaceColor,
        AlwaysOnTop,
        Enum.IsDefined(FillMode) ? FillMode : Default.FillMode,
        IsRgbHex(GradientEndColor) ? GradientEndColor.ToUpperInvariant() : Default.GradientEndColor);

    public static CollectionWindowAppearance Resolve(
        CollectionWindowAppearance globalAppearance,
        CollectionWindowAppearance? windowOverride)
    {
        ArgumentNullException.ThrowIfNull(globalAppearance);
        var global = globalAppearance.Normalize();
        var material = (windowOverride ?? global).Normalize();
        return material with { SurfaceOpacity = global.SurfaceOpacity, AlwaysOnTop = false };
    }

    private static bool IsRgbHex(string? value) =>
        value is { Length: 7 }
        && value[0] == '#'
        && value.AsSpan(1).ToString().All(Uri.IsHexDigit);
}

public sealed record AdaptiveWindowColors(
    string SolidColor,
    string GradientStartColor,
    string GradientEndColor)
{
    public static AdaptiveWindowColors FromDesktopColors(
        byte primaryRed,
        byte primaryGreen,
        byte primaryBlue,
        byte secondaryRed,
        byte secondaryGreen,
        byte secondaryBlue)
    {
        var solid = Tone(primaryRed, primaryGreen, primaryBlue, 82, 10);
        var start = Tone(primaryRed, primaryGreen, primaryBlue, 108, 12);
        var end = Tone(secondaryRed, secondaryGreen, secondaryBlue, 48, 5);
        if (ColorDistance(start, end) < 90)
        {
            end = (Darken(start.Red), Darken(start.Green), Darken(start.Blue));
        }
        return new AdaptiveWindowColors(ToHex(solid), ToHex(start), ToHex(end));
    }

    private static (byte Red, byte Green, byte Blue) Tone(
        byte red, byte green, byte blue, byte targetMaximum, byte floor)
    {
        var sourceMaximum = Math.Max(red, Math.Max(green, blue));
        if (sourceMaximum == 0)
        {
            return (targetMaximum, targetMaximum, targetMaximum);
        }
        return (
            NormalizeChannel(red, sourceMaximum, targetMaximum, floor),
            NormalizeChannel(green, sourceMaximum, targetMaximum, floor),
            NormalizeChannel(blue, sourceMaximum, targetMaximum, floor));
    }

    private static int ColorDistance(
        (byte Red, byte Green, byte Blue) first,
        (byte Red, byte Green, byte Blue) second) =>
        Math.Abs(first.Red - second.Red) + Math.Abs(first.Green - second.Green) + Math.Abs(first.Blue - second.Blue);

    private static byte NormalizeChannel(byte channel, int sourceMaximum, byte targetMaximum, byte floor) =>
        (byte)Math.Clamp(
            Math.Round(floor + ((double)channel / sourceMaximum * (targetMaximum - floor))),
            byte.MinValue,
            byte.MaxValue);

    private static byte Darken(byte channel) => (byte)Math.Round(channel * 0.20);

    private static string ToHex((byte Red, byte Green, byte Blue) color) =>
        $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
}

public sealed record CollectionWindowItemOrder(
    Guid ZoneId,
    string RelativeDirectory,
    string[]? ItemNames = null)
{
    public IReadOnlyList<string> EffectiveItemNames => ItemNames ?? [];
}

public sealed record CollectionWindowsPreferences(
    CollectionWindowLayout[]? Layouts = null,
    CollectionWindowAppearance? Appearance = null,
    CollectionWindowItemOrder[]? ItemOrders = null)
{
    public IReadOnlyList<CollectionWindowLayout> EffectiveLayouts => Layouts ?? [];
    public IReadOnlyList<CollectionWindowItemOrder> EffectiveItemOrders => ItemOrders ?? [];
    public CollectionWindowAppearance EffectiveAppearance =>
        (Appearance ?? CollectionWindowAppearance.Default).Normalize();
}

public static class CollectionItemOrderResolver
{
    public static IReadOnlyList<T> Apply<T>(
        IReadOnlyList<T> items,
        IReadOnlyList<string> orderedNames,
        Func<T, string> nameSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(orderedNames);
        ArgumentNullException.ThrowIfNull(nameSelector);
        var positions = orderedNames
            .Select((name, index) => (name, index))
            .Where(item => !string.IsNullOrWhiteSpace(item.name))
            .GroupBy(item => item.name, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.CurrentCultureIgnoreCase);

        return items
            .Select((item, defaultIndex) => new
            {
                Item = item,
                DefaultIndex = defaultIndex,
                SavedIndex = positions.GetValueOrDefault(nameSelector(item), int.MaxValue)
            })
            .OrderBy(item => item.SavedIndex)
            .ThenBy(item => item.DefaultIndex)
            .Select(item => item.Item)
            .ToArray();
    }
}

public static class CollectionZoneCatalog
{
    private const string ZoneNamespace = "DesktopManager.CollectionZone\0";

    public static IReadOnlyList<CollectionZone> Build(IReadOnlyList<OrganizationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules
            .Select(rule => (Rule: rule, Directory: NormalizeRelativeDirectory(rule.RelativeDestination)))
            .GroupBy(item => item.Directory, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var groupedRules = group.Select(item => item.Rule).ToArray();
                return new CollectionZone(
                    CreateStableId(group.Key),
                    CreateName(group.Key, groupedRules),
                    group.Key,
                    groupedRules.Select(rule => rule.Id).ToArray(),
                    groupedRules.Any(rule => rule.IsEnabled));
            })
            .OrderBy(zone => zone.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(zone => zone.RelativeDirectory, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string NormalizeRelativeDirectory(string relativeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDirectory);
        if (Path.IsPathRooted(relativeDirectory))
        {
            throw new InvalidOperationException("收纳区必须位于托管目录内。");
        }

        var segments = relativeDirectory
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("收纳区必须是有效的相对子目录。");
        }

        return Path.Combine(segments);
    }

    private static Guid CreateStableId(string relativeDirectory)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            ZoneNamespace + relativeDirectory.ToUpperInvariant()));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static string CreateName(
        string relativeDirectory,
        IReadOnlyList<OrganizationRule> rules)
    {
        var leafName = Path.GetFileName(relativeDirectory.TrimEnd(Path.DirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(leafName))
        {
            return leafName;
        }

        return rules.Count == 1 ? rules[0].Name : "收纳区";
    }
}
