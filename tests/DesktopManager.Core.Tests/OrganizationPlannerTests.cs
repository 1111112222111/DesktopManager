using System.Diagnostics;
using DesktopManager.Core;

namespace DesktopManager.Core.Tests;

public sealed class OrganizationPlannerTests
{
    [Fact]
    [Trait("Category", "Performance")]
    public void CreatePlan_WithOneThousandItems_CompletesWithinOneSecond()
    {
        var modifiedAt = DateTimeOffset.UtcNow;
        var items = Enumerable.Range(0, 1_000)
            .Select(index => new DesktopItem(
                Guid.NewGuid(),
                DesktopItemKind.File,
                $@"C:\Desktop\item-{index:D4}.txt",
                1_024,
                modifiedAt))
            .ToArray();
        var rules = new[]
        {
            new OrganizationRule(Guid.NewGuid(), "文档", 100, [".txt"], "文档"),
            new OrganizationRule(Guid.NewGuid(), "图片", 90, [".png", ".jpg"], "图片"),
            new OrganizationRule(Guid.NewGuid(), "压缩包", 80, [".zip"], "压缩包")
        };

        var stopwatch = Stopwatch.StartNew();
        var plan = OrganizationPlanner.CreatePlan(items, rules, @"D:\Archive", modifiedAt);
        stopwatch.Stop();

        Assert.Equal(1_000, plan.Items.Count);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"规则计划耗时 {stopwatch.Elapsed.TotalMilliseconds:N0}ms，超过 1000ms 上限。");
    }

    [Fact]
    public void CreatePlan_ReadOnlyDesktopItem_IsVisibleToPreviewButNeverExecutable()
    {
        var item = new DesktopItem(
            Guid.NewGuid(),
            DesktopItemKind.File,
            @"C:\Users\Public\Desktop\shared.txt",
            100,
            DateTimeOffset.UtcNow,
            IsReadOnly: true);
        var rule = new OrganizationRule(
            Guid.NewGuid(), "文档", 100, [".txt"], "文档");

        var impact = OrganizationPlanner.PreviewRuleImpact(rule, [item]);
        var plan = OrganizationPlanner.CreatePlan([item], [rule], @"D:\Managed");

        Assert.Single(impact.MatchedItems);
        Assert.Equal(1, impact.ReadOnlyItemCount);
        Assert.Empty(plan.Items);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void CreatePlan_WhenExtensionMatches_ProposesArchiveWithoutChangingSource()
    {
        var item = new DesktopItem(
            Id: Guid.NewGuid(),
            Kind: DesktopItemKind.File,
            Path: @"C:\Desktop\屏幕截图.png",
            Size: 1_024,
            ModifiedAt: new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.FromHours(8)));
        var rule = new OrganizationRule(
            Id: Guid.NewGuid(),
            Name: "截图归档",
            Priority: 100,
            Extensions: [".png", ".jpg"],
            RelativeDestination: @"图片\截图");

        var plan = OrganizationPlanner.CreatePlan(
            [item],
            [rule],
            @"D:\桌面归档");

        var planItem = Assert.Single(plan.Items);
        Assert.Equal(item.Path, planItem.SourcePath);
        Assert.Equal(@"D:\桌面归档\图片\截图\屏幕截图.png", planItem.TargetPath);
        Assert.Equal(SuggestedAction.Archive, planItem.SuggestedAction);
        Assert.Equal("截图归档", planItem.Explanation);
        Assert.Equal(PlanStatus.Draft, plan.Status);
    }

    [Fact]
    public void CreatePlan_WhenRuleHasNoConditions_MatchesEveryDesktopItem()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var items = new[]
        {
            new DesktopItem(Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\说明.txt", 10, observedAt),
            new DesktopItem(Guid.NewGuid(), DesktopItemKind.Folder, @"C:\Desktop\素材", 0, observedAt),
            new DesktopItem(Guid.NewGuid(), DesktopItemKind.Shortcut, @"C:\Desktop\工具.lnk", 20, observedAt)
        };
        var rule = new OrganizationRule(
            Guid.NewGuid(),
            "全部收纳",
            100,
            [],
            "未分类");

        var plan = OrganizationPlanner.CreatePlan(items, [rule], @"D:\Managed", observedAt);

        Assert.Equal(3, plan.Items.Count);
        Assert.All(plan.Items, item => Assert.StartsWith(@"D:\Managed\未分类", item.TargetPath));
    }

    [Fact]
    public void Validate_WhenSourceChangedAfterPlanning_ReportsExpiredPlan()
    {
        var observedAt = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.FromHours(8));
        var originalItem = new DesktopItem(
            Guid.NewGuid(),
            DesktopItemKind.File,
            @"C:\Desktop\report.txt",
            1_024,
            observedAt);
        var plan = OrganizationPlanner.CreatePlan(
            [originalItem],
            [new OrganizationRule(Guid.NewGuid(), "文档归档", 100, [".txt"], "文档")],
            @"D:\Archive");
        var changedItem = originalItem with
        {
            Size = 2_048,
            ModifiedAt = observedAt.AddMinutes(1)
        };
        var currentSnapshot = new DesktopSnapshot(
            @"C:\Desktop",
            observedAt.AddMinutes(1),
            [changedItem]);

        var validation = OrganizationPlanner.Validate(plan, currentSnapshot);

        Assert.False(validation.IsValid);
        var issue = Assert.Single(validation.Issues);
        Assert.Equal(PlanValidationIssueKind.SourceChanged, issue.Kind);
        Assert.Equal(originalItem.Id, issue.DesktopItemId);
    }

    [Fact]
    public void CreatePlan_WhenMatchingRuleIsDisabled_DoesNotProposeArchive()
    {
        var item = new DesktopItem(
            Guid.NewGuid(),
            DesktopItemKind.File,
            @"C:\Desktop\report.txt",
            1_024,
            DateTimeOffset.UtcNow);
        var disabledRule = new OrganizationRule(
            Guid.NewGuid(),
            "文档归档",
            100,
            [".txt"],
            "文档",
            IsEnabled: false);

        var plan = OrganizationPlanner.CreatePlan([item], [disabledRule], @"D:\Archive");

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void CreatePlan_WhenExtensionAndFileNameConditionsExist_RequiresBoth()
    {
        var modifiedAt = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.FromHours(8));
        var invoice = new DesktopItem(
            Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\八月发票.pdf", 2_048, modifiedAt);
        var ordinaryPdf = new DesktopItem(
            Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\会议资料.pdf", 2_048, modifiedAt);
        var rule = new OrganizationRule(
            Guid.NewGuid(),
            "发票归档",
            100,
            [".pdf"],
            "财务",
            FileNameKeywords: ["发票"]);

        var plan = OrganizationPlanner.CreatePlan(
            [invoice, ordinaryPdf],
            [rule],
            @"D:\Archive");

        var planItem = Assert.Single(plan.Items);
        Assert.Equal(invoice.Id, planItem.DesktopItemId);
    }

    [Fact]
    public void CreatePlan_WhenSizeRangeExists_IncludesBothBoundaries()
    {
        var modifiedAt = DateTimeOffset.UtcNow;
        var below = new DesktopItem(Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\below.bin", 999, modifiedAt);
        var minimum = new DesktopItem(Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\minimum.bin", 1_000, modifiedAt);
        var maximum = new DesktopItem(Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\maximum.bin", 2_000, modifiedAt);
        var above = new DesktopItem(Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\above.bin", 2_001, modifiedAt);
        var rule = new OrganizationRule(
            Guid.NewGuid(),
            "中型文件",
            100,
            [".bin"],
            "中型文件",
            MinimumSizeBytes: 1_000,
            MaximumSizeBytes: 2_000);

        var plan = OrganizationPlanner.CreatePlan(
            [below, minimum, maximum, above],
            [rule],
            @"D:\Archive");

        Assert.Equal([minimum.Id, maximum.Id], plan.Items.Select(item => item.DesktopItemId));
    }

    [Fact]
    public void CreatePlan_WhenItemKindConditionExists_CanMatchFoldersWithoutExtension()
    {
        var modifiedAt = DateTimeOffset.UtcNow;
        var folder = new DesktopItem(
            Guid.NewGuid(), DesktopItemKind.Folder, @"C:\Desktop\设计项目", 0, modifiedAt);
        var file = new DesktopItem(
            Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\设计项目.txt", 100, modifiedAt);
        var rule = new OrganizationRule(
            Guid.NewGuid(),
            "项目文件夹",
            100,
            [],
            "项目",
            ItemKinds: [DesktopItemKind.Folder]);

        var plan = OrganizationPlanner.CreatePlan([folder, file], [rule], @"D:\Archive");

        var planItem = Assert.Single(plan.Items);
        Assert.Equal(folder.Id, planItem.DesktopItemId);
    }

    [Fact]
    public void CreatePlan_WhenModifiedWithinDaysExists_IncludesThresholdInstant()
    {
        var plannedAt = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.FromHours(8));
        var threshold = new DesktopItem(
            Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\threshold.txt", 100, plannedAt.AddDays(-7));
        var tooOld = new DesktopItem(
            Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\old.txt", 100, plannedAt.AddDays(-7).AddSeconds(-1));
        var rule = new OrganizationRule(
            Guid.NewGuid(),
            "最近文档",
            100,
            [".txt"],
            "最近",
            ModifiedWithinDays: 7);

        var plan = OrganizationPlanner.CreatePlan(
            [threshold, tooOld],
            [rule],
            @"D:\Archive",
            plannedAt);

        var planItem = Assert.Single(plan.Items);
        Assert.Equal(threshold.Id, planItem.DesktopItemId);
    }

    [Fact]
    public void CreatePlan_WhenHighestPriorityRulesChooseDifferentTargets_ReportsConflict()
    {
        var item = new DesktopItem(
            Guid.NewGuid(),
            DesktopItemKind.File,
            @"C:\Desktop\屏幕截图.png",
            1_024,
            DateTimeOffset.UtcNow);
        var picturesRule = new OrganizationRule(
            Guid.NewGuid(), "图片归档", 100, [".png"], "图片");
        var evidenceRule = new OrganizationRule(
            Guid.NewGuid(), "证据归档", 100, [".png"], "项目证据");
        var lowerRule = new OrganizationRule(
            Guid.NewGuid(), "低优先级", 50, [".png"], "其他");

        var plan = OrganizationPlanner.CreatePlan(
            [item],
            [picturesRule, evidenceRule, lowerRule],
            @"D:\Archive");

        Assert.Empty(plan.Items);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(item.Id, conflict.DesktopItemId);
        Assert.Equal(100, conflict.Priority);
        Assert.Equal(
            [@"D:\Archive\图片\屏幕截图.png", @"D:\Archive\项目证据\屏幕截图.png"],
            conflict.Choices.Select(choice => choice.TargetPath));
    }

    [Fact]
    public void CreatePlan_WhenHighestPriorityRulesChooseSameTarget_CoalescesTheirExplanation()
    {
        var item = new DesktopItem(
            Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\截图.png", 100, DateTimeOffset.UtcNow);
        var plan = OrganizationPlanner.CreatePlan(
            [item],
            [
                new OrganizationRule(Guid.NewGuid(), "图片归档", 100, [".png"], "图片"),
                new OrganizationRule(Guid.NewGuid(), "截图归档", 100, [".png"], "图片")
            ],
            @"D:\Archive");

        Assert.Empty(plan.Conflicts);
        var planItem = Assert.Single(plan.Items);
        Assert.Equal("图片归档、截图归档", planItem.Explanation);
    }

    [Fact]
    public void ResolveConflict_WhenCandidateBelongsToConflict_AddsSafePlanItem()
    {
        var modifiedAt = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.FromHours(8));
        var item = new DesktopItem(
            Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\截图.png", 1_024, modifiedAt);
        var selectedRule = new OrganizationRule(
            Guid.NewGuid(), "图片归档", 100, [".png"], "图片");
        var otherRule = new OrganizationRule(
            Guid.NewGuid(), "证据归档", 100, [".png"], "证据");
        var plan = OrganizationPlanner.CreatePlan(
            [item], [selectedRule, otherRule], @"D:\Archive", modifiedAt);

        var resolved = OrganizationPlanner.ResolveConflict(plan, item.Id, selectedRule.Id);

        Assert.Equal(plan.Id, resolved.Id);
        Assert.Equal(PlanStatus.Draft, resolved.Status);
        Assert.Empty(resolved.Conflicts);
        var planItem = Assert.Single(resolved.Items);
        Assert.Equal(@"D:\Archive\图片\截图.png", planItem.TargetPath);
        Assert.Equal("用户裁决：图片归档", planItem.Explanation);
        Assert.Equal(item.Size, planItem.ObservedSize);
        Assert.Equal(item.ModifiedAt, planItem.ObservedModifiedAt);
    }

    [Fact]
    public void CreatePlan_WhenItemIsKept_LeavesItVisibleButDoesNotSuggestAction()
    {
        var modifiedAt = DateTimeOffset.UtcNow;
        var keptItem = new DesktopItem(
            Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\常用.txt", 100, modifiedAt);
        var inboxItem = new DesktopItem(
            Guid.NewGuid(), DesktopItemKind.File, @"C:\Desktop\待整理.txt", 100, modifiedAt);
        var dispositions = DesktopItemDispositionPolicy.Empty.WithDisposition(
            keptItem.Path,
            DesktopItemDisposition.Keep);

        var plan = OrganizationPlanner.CreatePlan(
            [keptItem, inboxItem],
            [new OrganizationRule(Guid.NewGuid(), "文档", 100, [".txt"], "文档")],
            @"D:\Archive",
            dispositionPolicy: dispositions);

        var planItem = Assert.Single(plan.Items);
        Assert.Equal(inboxItem.Id, planItem.DesktopItemId);
    }
}
