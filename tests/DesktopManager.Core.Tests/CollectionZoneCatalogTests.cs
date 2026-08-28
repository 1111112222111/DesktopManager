using DesktopManager.Core;

namespace DesktopManager.Core.Tests;

public sealed class CollectionZoneCatalogTests
{
    [Fact]
    public void WindowAppearance_NormalizesOpacityAndColor()
    {
        var normalized = new CollectionWindowAppearance(
            SurfaceOpacity: 2,
            SurfaceColor: "not-a-color",
            AlwaysOnTop: true,
            FillMode: (CollectionWindowFillMode)999,
            GradientEndColor: "bad").Normalize();

        Assert.Equal(0.96, normalized.SurfaceOpacity);
        Assert.Equal("#232B28", normalized.SurfaceColor);
        Assert.True(normalized.AlwaysOnTop);
        Assert.Equal(CollectionWindowFillMode.Solid, normalized.FillMode);
        Assert.Equal("#151B19", normalized.GradientEndColor);
    }

    [Fact]
    public void WindowLayout_KeepsItsOwnNormalizedAppearanceOverride()
    {
        var layout = new CollectionWindowLayout(
            Guid.NewGuid(),
            40,
            60,
            Appearance: new CollectionWindowAppearance(
                0.42,
                "#204060",
                FillMode: CollectionWindowFillMode.Gradient,
                GradientEndColor: "#102030"));

        var appearance = Assert.IsType<CollectionWindowAppearance>(layout.Appearance).Normalize();

        Assert.Equal(0.42, appearance.SurfaceOpacity);
        Assert.Equal("#204060", appearance.SurfaceColor);
        Assert.Equal(CollectionWindowFillMode.Gradient, appearance.FillMode);
        Assert.Equal("#102030", appearance.GradientEndColor);
    }

    [Fact]
    public void WindowAppearanceResolve_UsesGlobalOpacityWithLocalMaterialAndColor()
    {
        var global = new CollectionWindowAppearance(
            0.64,
            "#232B28",
            FillMode: CollectionWindowFillMode.Solid);
        var local = new CollectionWindowAppearance(
            0.24,
            "#31475A",
            FillMode: CollectionWindowFillMode.Gradient,
            GradientEndColor: "#182D38");

        var resolved = CollectionWindowAppearance.Resolve(global, local);

        Assert.Equal(0.64, resolved.SurfaceOpacity);
        Assert.Equal("#31475A", resolved.SurfaceColor);
        Assert.Equal(CollectionWindowFillMode.Gradient, resolved.FillMode);
        Assert.Equal("#182D38", resolved.GradientEndColor);
    }

    [Fact]
    public void AdaptiveWindowColors_ProducesDarkDistinctWallpaperDerivedColors()
    {
        var colors = AdaptiveWindowColors.FromDesktopColors(235, 190, 92, 72, 140, 210);

        Assert.Matches("^#[0-9A-F]{6}$", colors.SolidColor);
        Assert.Matches("^#[0-9A-F]{6}$", colors.GradientStartColor);
        Assert.Matches("^#[0-9A-F]{6}$", colors.GradientEndColor);
        Assert.NotEqual(colors.GradientStartColor, colors.GradientEndColor);
        var start = ParseRgb(colors.GradientStartColor);
        var end = ParseRgb(colors.GradientEndColor);
        Assert.True(
            Math.Abs(start.Red - end.Red) + Math.Abs(start.Green - end.Green) + Math.Abs(start.Blue - end.Blue) >= 90,
            $"渐变两端差异不足：{colors.GradientStartColor} → {colors.GradientEndColor}");
        Assert.All(new[] { colors.SolidColor, colors.GradientStartColor, colors.GradientEndColor }, color =>
        {
            var red = Convert.ToByte(color.Substring(1, 2), 16);
            var green = Convert.ToByte(color.Substring(3, 2), 16);
            var blue = Convert.ToByte(color.Substring(5, 2), 16);
            Assert.True(Math.Max(red, Math.Max(green, blue)) <= 112);
        });
    }

    private static (byte Red, byte Green, byte Blue) ParseRgb(string color) =>
        (Convert.ToByte(color.Substring(1, 2), 16),
         Convert.ToByte(color.Substring(3, 2), 16),
         Convert.ToByte(color.Substring(5, 2), 16));

    [Fact]
    public void Build_MergesRulesThatUseTheSameDestination()
    {
        var first = CreateRule("Word", Path.Combine("工作", "文档"), enabled: true);
        var second = CreateRule("Excel", Path.Combine("工作", "文档"), enabled: false);

        var zone = Assert.Single(CollectionZoneCatalog.Build([first, second]));

        Assert.Equal("文档", zone.Name);
        Assert.Equal(Path.Combine("工作", "文档"), zone.RelativeDirectory);
        Assert.Equal([first.Id, second.Id], zone.RuleIds);
        Assert.True(zone.HasEnabledRule);
    }

    [Fact]
    public void Build_ProducesStableIdIndependentOfRuleOrderAndCase()
    {
        var first = Assert.Single(CollectionZoneCatalog.Build([
            CreateRule("图片", Path.Combine("媒体", "图片"), enabled: true)]));
        var second = Assert.Single(CollectionZoneCatalog.Build([
            CreateRule("另一个名称", Path.Combine("媒体", "图片").ToUpperInvariant(), enabled: true)]));

        Assert.Equal(first.Id, second.Id);
    }

    [Theory]
    [InlineData("..\\外部")]
    [InlineData("C:\\外部")]
    [InlineData(".")]
    public void Build_RejectsDestinationOutsideManagedRoot(string destination)
    {
        Assert.ThrowsAny<Exception>(() => CollectionZoneCatalog.Build([
            CreateRule("非法", destination, enabled: true)]));
    }

    private static OrganizationRule CreateRule(string name, string destination, bool enabled) =>
        new(Guid.NewGuid(), name, 100, [".txt"], destination, enabled);
}
