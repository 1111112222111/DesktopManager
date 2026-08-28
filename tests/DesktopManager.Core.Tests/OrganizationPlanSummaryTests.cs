namespace DesktopManager.Core.Tests;

public sealed class OrganizationPlanSummaryTests
{
    [Fact]
    public void Summarize_GroupsTargetsAndSeparatesUnknownFolderSize()
    {
        var plan = new OrganizationPlan(
            Guid.NewGuid(),
            PlanStatus.Draft,
            [
                Item("a.txt", "文档", 1_024, DesktopItemKind.File),
                Item("b.txt", "文档", 2_048, DesktopItemKind.File),
                Item("素材", "素材", 99_999, DesktopItemKind.Folder)
            ],
            DetectedExcludedItemIds: [Guid.NewGuid()]);

        var summary = OrganizationPlanAnalyzer.Summarize(plan);

        Assert.Equal(3, summary.ExecutableItemCount);
        Assert.Equal(1, summary.ExcludedItemCount);
        Assert.Equal(3_072, summary.KnownTotalSizeBytes);
        Assert.Equal(1, summary.UnknownSizeItemCount);
        Assert.Equal(2, summary.TargetDistribution.Count);
        Assert.Equal(2, summary.TargetDistribution[0].ItemCount);
    }

    [Fact]
    public void Summarize_CountsDuplicateFinalTargetsCaseInsensitively()
    {
        var first = Item("same.txt", "文档", 100, DesktopItemKind.File);
        var second = Item("other.txt", "文档", 100, DesktopItemKind.File) with
        {
            TargetPath = first.TargetPath.ToUpperInvariant()
        };
        var plan = new OrganizationPlan(Guid.NewGuid(), PlanStatus.Draft, [first, second]);

        var summary = OrganizationPlanAnalyzer.Summarize(plan);

        Assert.Equal(1, summary.DuplicateTargetCount);
    }

    [Fact]
    public void Summarize_PlanCreatedForFolderDoesNotTreatFolderSizeAsKnown()
    {
        var folder = new DesktopItem(
            Guid.NewGuid(),
            DesktopItemKind.Folder,
            "C:\\Desktop\\项目资料",
            50_000,
            DateTimeOffset.UtcNow);
        var rule = new OrganizationRule(
            Guid.NewGuid(),
            "文件夹归档",
            100,
            [],
            "文件夹",
            ItemKinds: [DesktopItemKind.Folder]);
        var plan = OrganizationPlanner.CreatePlan([folder], [rule], "D:\\Managed");

        var summary = OrganizationPlanAnalyzer.Summarize(plan);

        Assert.Equal(0, summary.KnownTotalSizeBytes);
        Assert.Equal(1, summary.UnknownSizeItemCount);
    }

    private static PlanItem Item(
        string name,
        string destination,
        long size,
        DesktopItemKind kind) => new(
        Guid.NewGuid(),
        Path.Combine("C:\\Desktop", name),
        Path.Combine("D:\\Managed", destination, name),
        SuggestedAction.Archive,
        "测试",
        size,
        DateTimeOffset.UtcNow,
        kind);
}
