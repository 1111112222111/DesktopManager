namespace DesktopManager.Core.Tests;

public sealed class BackupRestorePlannerTests
{
    [Fact]
    public void Create_SkipsHistoryAndPreferencesOutsideCurrentRoots()
    {
        var demoSource = Path.GetFullPath("C:\\Current\\DemoDesktop");
        var demoManaged = Path.GetFullPath("C:\\Current\\DemoManaged");
        var realSource = Path.GetFullPath("C:\\Users\\Current\\Desktop");
        var realManaged = Path.GetFullPath("D:\\CurrentManaged");
        var package = new BackupPackage(
            new BackupManifest(BackupPackageFormat.CurrentVersion, DateTimeOffset.UtcNow, "test"),
            new BackupSettings(
                realManaged,
                [new OrganizationRule(Guid.NewGuid(), "文档", 10, [".txt"], "文档")],
                NotificationPreferences.Default,
                [
                    new DesktopItemPreference(Path.Combine(realSource, "keep.txt"), DesktopItemDisposition.Keep),
                    new DesktopItemPreference("E:\\Other\\ignore.txt", DesktopItemDisposition.Ignore)
                ],
                null,
                [new FavoriteCollection(
                    Guid.NewGuid(),
                    "工作",
                    [Path.Combine(realSource, "keep.txt"), "E:\\Other\\favorite.txt"])]),
            [
                Scoped(OperationScope.RealDesktop, Path.Combine(realSource, "a.txt"), Path.Combine(realManaged, "文档", "a.txt")),
                Scoped(OperationScope.RealDesktop, "E:\\Other\\b.txt", Path.Combine(realManaged, "文档", "b.txt"))
            ]);

        var plan = BackupRestorePlanner.Create(
            package, demoSource, demoManaged, realSource, realManaged);

        Assert.Single(plan.ItemPreferences);
        Assert.Single(plan.Favorites);
        Assert.Single(plan.Favorites[0].ItemPaths);
        Assert.Single(plan.Operations);
        Assert.Equal(1, plan.SkippedItemPreferenceCount);
        Assert.Equal(1, plan.SkippedFavoriteMemberCount);
        Assert.Equal(1, plan.SkippedOperationCount);
    }

    [Fact]
    public void Create_RejectsRuleDestinationThatEscapesManagedRoot()
    {
        var package = new BackupPackage(
            new BackupManifest(BackupPackageFormat.CurrentVersion, DateTimeOffset.UtcNow, "test"),
            new BackupSettings(
                null,
                [new OrganizationRule(Guid.NewGuid(), "恶意规则", 10, [".txt"], "..\\outside")],
                NotificationPreferences.Default,
                []),
            []);

        Assert.Throws<InvalidDataException>(() => BackupRestorePlanner.Create(
            package,
            "C:\\Current\\DemoDesktop",
            "C:\\Current\\DemoManaged",
            "C:\\Users\\Current\\Desktop",
            "D:\\CurrentManaged"));
    }

    [Fact]
    public void Create_KeepsUndoHistoryOnlyWhenItsOriginalOperationIsSafe()
    {
        var demoSource = "C:\\DemoDesktop";
        var demoManaged = "C:\\DemoManaged";
        var original = Scoped(
            OperationScope.Demo,
            Path.Combine(demoSource, "a.txt"),
            Path.Combine(demoManaged, "a.txt"));
        var undo = new ScopedOrganizationOperation(
            OperationScope.Demo,
            new OrganizationOperation(
                Guid.NewGuid(),
                original.Operation.PlanId,
                OperationStatus.Completed,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                [new OperationItem(
                    Path.Combine(demoManaged, "a.txt"),
                    Path.Combine(demoSource, "a.txt"),
                    OperationItemStatus.Succeeded,
                    null)],
                OperationKind.Undo,
                original.Operation.Id));
        var package = new BackupPackage(
            new BackupManifest(BackupPackageFormat.CurrentVersion, DateTimeOffset.UtcNow, "test"),
            new BackupSettings(
                null,
                [new OrganizationRule(Guid.NewGuid(), "规则", 10, [".txt"], "文档")],
                NotificationPreferences.Default,
                []),
            [original, undo]);

        var plan = BackupRestorePlanner.Create(
            package,
            demoSource,
            demoManaged,
            "C:\\RealDesktop",
            "D:\\RealManaged");

        Assert.Equal(2, plan.Operations.Length);
        Assert.Contains(plan.Operations, item => item.Operation.Kind is OperationKind.Undo);
    }

    [Fact]
    public void Create_RestoresOnlyCollectionWindowLayoutsForCurrentRuleZones()
    {
        var rule = new OrganizationRule(Guid.NewGuid(), "文档", 10, [".txt"], "文档");
        var zone = Assert.Single(CollectionZoneCatalog.Build([rule]));
        var validLayout = new CollectionWindowLayout(
            zone.Id, 100, 120, 420, 320, AccentColor: "#2878B5", Title: "我的文档");
        var package = new BackupPackage(
            new BackupManifest(BackupPackageFormat.CurrentVersion, DateTimeOffset.UtcNow, "test"),
            new BackupSettings(
                null,
                [rule],
                NotificationPreferences.Default,
                [],
                CollectionWindows: new CollectionWindowsPreferences(
                    [validLayout, validLayout with { ZoneId = Guid.NewGuid() }],
                    new CollectionWindowAppearance(0.72, "#20272E", true),
                    [
                        new CollectionWindowItemOrder(zone.Id, "", ["B.txt", "A.txt"]),
                        new CollectionWindowItemOrder(Guid.NewGuid(), "", ["invalid.txt"])
                    ])),
            []);

        var plan = BackupRestorePlanner.Create(
            package,
            "C:\\DemoDesktop",
            "C:\\DemoManaged",
            "C:\\RealDesktop",
            "D:\\RealManaged");

        var restored = Assert.Single(plan.CollectionWindows.EffectiveLayouts);
        Assert.Equal(zone.Id, restored.ZoneId);
        Assert.Equal("我的文档", restored.Title);
        Assert.Equal("#2878B5", restored.AccentColor);
        Assert.Equal(0.72, plan.CollectionWindows.EffectiveAppearance.SurfaceOpacity);
        Assert.Equal("#20272E", plan.CollectionWindows.EffectiveAppearance.SurfaceColor);
        Assert.True(plan.CollectionWindows.EffectiveAppearance.AlwaysOnTop);
        var restoredOrder = Assert.Single(plan.CollectionWindows.EffectiveItemOrders);
        Assert.Equal(zone.Id, restoredOrder.ZoneId);
        Assert.Equal(["B.txt", "A.txt"], restoredOrder.EffectiveItemNames);
    }

    private static ScopedOrganizationOperation Scoped(
        OperationScope scope,
        string sourcePath,
        string targetPath) => new(
            scope,
            new OrganizationOperation(
                Guid.NewGuid(), Guid.NewGuid(), OperationStatus.Completed,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                [new OperationItem(sourcePath, targetPath, OperationItemStatus.Succeeded, null)]));
}
