namespace DesktopManager.Core.Tests;

public sealed class OrganizationPlanEditingTests
{
    [Fact]
    public void ExcludeItems_RemovesOnlyExecutableItemsAndKeepsConflicts()
    {
        var first = PlanItem("a.txt");
        var second = PlanItem("b.txt");
        var conflict = new RuleConflict(
            Guid.NewGuid(), "C:\\Desktop\\conflict.txt", 10,
            [new RuleConflictChoice(Guid.NewGuid(), "规则", SuggestedAction.Archive, "D:\\Managed\\conflict.txt")]);
        var plan = new OrganizationPlan(Guid.NewGuid(), PlanStatus.Draft, [first, second], [conflict]);

        var edited = OrganizationPlanner.ExcludeItems(plan, [first.DesktopItemId]);

        Assert.Equal([second], edited.Items);
        Assert.Equal([first.DesktopItemId], edited.ExcludedItemIds);
        Assert.Single(edited.Conflicts);
    }

    [Fact]
    public void KeepOnlyItems_RecordsAllRemovedItemsAsExcluded()
    {
        var first = PlanItem("a.txt");
        var second = PlanItem("b.txt");
        var plan = new OrganizationPlan(Guid.NewGuid(), PlanStatus.Draft, [first, second]);

        var edited = OrganizationPlanner.KeepOnlyItems(plan, [second.DesktopItemId]);

        Assert.Equal([second], edited.Items);
        Assert.Equal([first.DesktopItemId], edited.ExcludedItemIds);
    }

    [Fact]
    public void KeepOnlyItems_RejectsPlanWithUnresolvedConflicts()
    {
        var item = PlanItem("a.txt");
        var conflict = new RuleConflict(Guid.NewGuid(), "C:\\Desktop\\x.txt", 10, []);
        var plan = new OrganizationPlan(Guid.NewGuid(), PlanStatus.Draft, [item], [conflict]);

        Assert.Throws<InvalidOperationException>(() =>
            OrganizationPlanner.KeepOnlyItems(plan, [item.DesktopItemId]));
    }

    [Fact]
    public void AdjustTarget_ChangesSubdirectoryButPreservesFileName()
    {
        var item = PlanItem("a.txt");
        var plan = new OrganizationPlan(Guid.NewGuid(), PlanStatus.Draft, [item]);

        var edited = OrganizationPlanner.AdjustTarget(
            plan, item.DesktopItemId, "重要\\本周", "D:\\Managed");

        Assert.Equal(
            Path.GetFullPath("D:\\Managed\\重要\\本周\\a.txt"),
            edited.Items[0].TargetPath);
    }

    [Fact]
    public void AdjustTarget_RejectsDestinationOutsideManagedRoot()
    {
        var item = PlanItem("a.txt");
        var plan = new OrganizationPlan(Guid.NewGuid(), PlanStatus.Draft, [item]);

        Assert.Throws<InvalidOperationException>(() =>
            OrganizationPlanner.AdjustTarget(plan, item.DesktopItemId, "..\\Outside", "D:\\Managed"));
    }

    private static PlanItem PlanItem(string name) => new(
        Guid.NewGuid(),
        Path.GetFullPath(Path.Combine("C:\\Desktop", name)),
        Path.GetFullPath(Path.Combine("D:\\Managed", name)),
        SuggestedAction.Archive,
        "测试");
}
