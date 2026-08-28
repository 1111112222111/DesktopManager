using DesktopManager.Core;

namespace DesktopManager.Core.Tests;

public sealed class ExecutionGateTests
{
    [Fact]
    public void PrepareForExecution_WhenCurrentDraftWasReviewed_ReturnsConfirmedPlanWithoutTextConfirmation()
    {
        var observedAt = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.FromHours(8));
        var desktopItem = new DesktopItem(
            Guid.NewGuid(),
            DesktopItemKind.File,
            @"C:\Desktop\report.txt",
            1_024,
            observedAt);
        var plan = OrganizationPlanner.CreatePlan(
            [desktopItem],
            [new OrganizationRule(Guid.NewGuid(), "文档归档", 100, [".txt"], "文档")],
            @"D:\Archive");
        var snapshot = new DesktopSnapshot(@"C:\Desktop", observedAt, [desktopItem]);

        var review = ExecutionGate.Review(plan, snapshot);
        var confirmed = ExecutionGate.PrepareForExecution(review);

        Assert.True(review.CanExecute);
        Assert.Equal(1_024, review.Summary.KnownTotalSizeBytes);
        Assert.Equal("将移动 1 个桌面项目（已知大小 1 KB），分布到 1 个目标目录；目标重名时会安全重命名，不会覆盖已有文件。", review.RiskSummary);
        Assert.Equal(PlanStatus.Confirmed, confirmed.Status);
        Assert.Equal(plan.Id, confirmed.Id);
    }

    [Fact]
    public void Review_WhenPlanContainsRuleConflict_RejectsEntirePlan()
    {
        var observedAt = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.FromHours(8));
        var safeItem = new DesktopItem(
            Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\notes.txt", 100, observedAt);
        var conflictingItem = new DesktopItem(
            Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\image.png", 100, observedAt);
        var plan = OrganizationPlanner.CreatePlan(
            [safeItem, conflictingItem],
            [
                new OrganizationRule(Guid.NewGuid(), "文档", 100, [".txt"], "文档"),
                new OrganizationRule(Guid.NewGuid(), "图片", 100, [".png"], "图片"),
                new OrganizationRule(Guid.NewGuid(), "素材", 100, [".png"], "素材")
            ],
            @"D:\Archive",
            observedAt);
        var snapshot = new DesktopSnapshot(
            @"C:\Desktop",
            observedAt,
            [safeItem, conflictingItem]);

        var review = ExecutionGate.Review(plan, snapshot);

        Assert.Single(plan.Items);
        Assert.Single(plan.Conflicts);
        Assert.False(review.CanExecute);
        Assert.Contains("1 个规则冲突", review.RiskSummary);
    }
}
