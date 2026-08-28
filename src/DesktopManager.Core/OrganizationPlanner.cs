namespace DesktopManager.Core;

public enum DesktopItemKind
{
    File,
    Folder,
    Shortcut
}

public enum SuggestedAction
{
    Archive
}

public enum PlanStatus
{
    Draft,
    Confirmed,
    Expired
}

public sealed record DesktopItem(
    Guid Id,
    DesktopItemKind Kind,
    string Path,
    long Size,
    DateTimeOffset ModifiedAt,
    DateTimeOffset? CreatedAt = null,
    bool IsReadOnly = false);

public sealed record RuleImpactPreview(
    IReadOnlyList<DesktopItem> MatchedItems,
    long KnownTotalSizeBytes,
    int UnknownFolderSizeCount,
    int ReadOnlyItemCount);

public sealed record DesktopSnapshot(
    string SourceDirectory,
    DateTimeOffset ObservedAt,
    IReadOnlyList<DesktopItem> Items);

public sealed record OrganizationRule(
    Guid Id,
    string Name,
    int Priority,
    IReadOnlyList<string> Extensions,
    string RelativeDestination,
    bool IsEnabled = true,
    IReadOnlyList<string>? FileNameKeywords = null,
    long? MinimumSizeBytes = null,
    long? MaximumSizeBytes = null,
    IReadOnlyList<DesktopItemKind>? ItemKinds = null,
    int? ModifiedWithinDays = null);

public sealed record PlanItem(
    Guid DesktopItemId,
    string SourcePath,
    string TargetPath,
    SuggestedAction SuggestedAction,
    string Explanation,
    long ObservedSize = 0,
    DateTimeOffset? ObservedModifiedAt = null,
    DesktopItemKind ObservedKind = DesktopItemKind.File);

public sealed record RuleConflictChoice(
    Guid RuleId,
    string RuleName,
    SuggestedAction SuggestedAction,
    string TargetPath);

public sealed record RuleConflict(
    Guid DesktopItemId,
    string SourcePath,
    int Priority,
    IReadOnlyList<RuleConflictChoice> Choices,
    long ObservedSize = 0,
    DateTimeOffset? ObservedModifiedAt = null,
    DesktopItemKind ObservedKind = DesktopItemKind.File);

public sealed record OrganizationPlan(
    Guid Id,
    PlanStatus Status,
    IReadOnlyList<PlanItem> Items,
    IReadOnlyList<RuleConflict>? DetectedConflicts = null,
    IReadOnlyList<Guid>? DetectedExcludedItemIds = null)
{
    public IReadOnlyList<RuleConflict> Conflicts => DetectedConflicts ?? [];
    public IReadOnlyList<Guid> ExcludedItemIds => DetectedExcludedItemIds ?? [];
}

public enum PlanValidationIssueKind
{
    SourceMissing,
    SourceChanged
}

public sealed record PlanValidationIssue(
    Guid DesktopItemId,
    PlanValidationIssueKind Kind,
    string Message);

public sealed record PlanValidation(
    bool IsValid,
    IReadOnlyList<PlanValidationIssue> Issues);

public static class OrganizationPlanner
{
    public static OrganizationPlan CreatePlan(
        IReadOnlyList<DesktopItem> items,
        IReadOnlyList<OrganizationRule> rules,
        string managedDirectory,
        DateTimeOffset? plannedAt = null,
        DesktopItemDispositionPolicy? dispositionPolicy = null)
    {
        var effectivePlannedAt = plannedAt ?? DateTimeOffset.UtcNow;
        var effectiveDispositionPolicy = dispositionPolicy ?? DesktopItemDispositionPolicy.Empty;
        var planItems = new List<PlanItem>();
        var conflicts = new List<RuleConflict>();
        foreach (var item in items)
        {
            if (item.IsReadOnly)
            {
                continue;
            }
            if (effectiveDispositionPolicy.GetDisposition(item.Path)
                is not DesktopItemDisposition.Inbox)
            {
                continue;
            }

            var decision = CreatePlanningDecision(
                item,
                rules,
                managedDirectory,
                effectivePlannedAt);
            if (decision.PlanItem is not null)
            {
                planItems.Add(decision.PlanItem);
            }
            if (decision.Conflict is not null)
            {
                conflicts.Add(decision.Conflict);
            }
        }

        return new OrganizationPlan(Guid.NewGuid(), PlanStatus.Draft, planItems, conflicts);
    }

    public static RuleImpactPreview PreviewRuleImpact(
        OrganizationRule rule,
        IReadOnlyList<DesktopItem> items,
        DateTimeOffset? previewedAt = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(items);
        var effectivePreviewedAt = previewedAt ?? DateTimeOffset.UtcNow;
        var matched = items
            .Where(item => RuleMatches(
                rule,
                item,
                Path.GetExtension(item.Path),
                effectivePreviewedAt))
            .ToArray();
        var knownSize = matched
            .Where(item => item.Kind is not DesktopItemKind.Folder)
            .Aggregate(0L, (total, item) =>
                item.Size > long.MaxValue - total ? long.MaxValue : total + Math.Max(0, item.Size));
        return new RuleImpactPreview(
            matched,
            knownSize,
            matched.Count(item => item.Kind is DesktopItemKind.Folder),
            matched.Count(item => item.IsReadOnly));
    }

    public static PlanValidation Validate(
        OrganizationPlan plan,
        DesktopSnapshot currentSnapshot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(currentSnapshot);

        var currentItems = currentSnapshot.Items.ToDictionary(
            item => System.IO.Path.GetFullPath(item.Path),
            StringComparer.OrdinalIgnoreCase);
        var issues = new List<PlanValidationIssue>();

        foreach (var planItem in plan.Items)
        {
            var sourcePath = System.IO.Path.GetFullPath(planItem.SourcePath);
            if (!currentItems.TryGetValue(sourcePath, out var currentItem))
            {
                issues.Add(new PlanValidationIssue(
                    planItem.DesktopItemId,
                    PlanValidationIssueKind.SourceMissing,
                    "源项目已不存在或已被重命名。"));
                continue;
            }

            if (currentItem.Size != planItem.ObservedSize
                || currentItem.ModifiedAt != planItem.ObservedModifiedAt)
            {
                issues.Add(new PlanValidationIssue(
                    planItem.DesktopItemId,
                    PlanValidationIssueKind.SourceChanged,
                    "源项目在计划生成后发生了变化。"));
            }
        }

        return new PlanValidation(issues.Count == 0, issues);
    }

    public static OrganizationPlan ResolveConflict(
        OrganizationPlan plan,
        Guid desktopItemId,
        Guid selectedRuleId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Status is not PlanStatus.Draft)
        {
            throw new InvalidOperationException("只有草稿整理计划可以进行冲突裁决。");
        }

        var conflict = plan.Conflicts.FirstOrDefault(candidate =>
            candidate.DesktopItemId == desktopItemId)
            ?? throw new InvalidOperationException("整理计划中不存在指定的规则冲突。");
        var choice = conflict.Choices.FirstOrDefault(candidate =>
            candidate.RuleId == selectedRuleId)
            ?? throw new InvalidOperationException("所选规则不属于该冲突的候选建议。");
        var resolvedItem = new PlanItem(
            conflict.DesktopItemId,
            conflict.SourcePath,
            choice.TargetPath,
            choice.SuggestedAction,
            $"用户裁决：{choice.RuleName}",
            conflict.ObservedSize,
            conflict.ObservedModifiedAt,
            conflict.ObservedKind);

        return plan with
        {
            Items = [.. plan.Items, resolvedItem],
            DetectedConflicts = plan.Conflicts
                .Where(candidate => candidate.DesktopItemId != desktopItemId)
                .ToArray()
        };
    }

    public static OrganizationPlan ExcludeItems(
        OrganizationPlan plan,
        IEnumerable<Guid> desktopItemIds)
    {
        EnsureDraft(plan);
        ArgumentNullException.ThrowIfNull(desktopItemIds);
        var selectedIds = desktopItemIds.ToHashSet();
        if (selectedIds.Count == 0)
        {
            throw new InvalidOperationException("至少选择一个可执行计划项。");
        }
        var existingIds = plan.Items.Select(item => item.DesktopItemId).ToHashSet();
        if (!selectedIds.IsSubsetOf(existingIds))
        {
            throw new InvalidOperationException("只能排除当前计划中的可执行项。");
        }
        return plan with
        {
            Items = plan.Items
                .Where(item => !selectedIds.Contains(item.DesktopItemId))
                .ToArray(),
            DetectedExcludedItemIds = plan.ExcludedItemIds
                .Concat(selectedIds)
                .Distinct()
                .ToArray()
        };
    }

    public static OrganizationPlan KeepOnlyItems(
        OrganizationPlan plan,
        IEnumerable<Guid> desktopItemIds)
    {
        EnsureDraft(plan);
        ArgumentNullException.ThrowIfNull(desktopItemIds);
        if (plan.Conflicts.Count > 0)
        {
            throw new InvalidOperationException("存在未裁决规则冲突时不能批量选择执行项。");
        }
        var selectedIds = desktopItemIds.ToHashSet();
        if (selectedIds.Count == 0)
        {
            throw new InvalidOperationException("至少选择一个可执行计划项。");
        }
        var existingIds = plan.Items.Select(item => item.DesktopItemId).ToHashSet();
        if (!selectedIds.IsSubsetOf(existingIds))
        {
            throw new InvalidOperationException("只能保留当前计划中的可执行项。");
        }
        return plan with
        {
            Items = plan.Items
                .Where(item => selectedIds.Contains(item.DesktopItemId))
                .ToArray(),
            DetectedExcludedItemIds = plan.ExcludedItemIds
                .Concat(existingIds.Where(id => !selectedIds.Contains(id)))
                .Distinct()
                .ToArray()
        };
    }

    public static OrganizationPlan AdjustTarget(
        OrganizationPlan plan,
        Guid desktopItemId,
        string relativeDestination,
        string managedDirectory)
    {
        EnsureDraft(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDestination);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedDirectory);
        var item = plan.Items.FirstOrDefault(candidate => candidate.DesktopItemId == desktopItemId)
            ?? throw new InvalidOperationException("只能调整当前计划中的可执行项。");
        if (Path.IsPathRooted(relativeDestination))
        {
            throw new InvalidOperationException("计划目标必须是托管目录内的相对子目录。");
        }

        var managedRoot = Path.GetFullPath(managedDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var targetDirectory = Path.GetFullPath(relativeDestination.Trim(), managedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(targetDirectory, managedRoot, StringComparison.OrdinalIgnoreCase)
            && !targetDirectory.StartsWith(
                managedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("计划目标越过托管目录边界。");
        }
        var targetPath = Path.Combine(targetDirectory, Path.GetFileName(item.SourcePath));
        return plan with
        {
            Items = plan.Items.Select(candidate =>
                candidate.DesktopItemId == desktopItemId
                    ? candidate with
                    {
                        TargetPath = targetPath,
                        Explanation = "用户调整当前计划目标"
                    }
                    : candidate).ToArray()
        };
    }

    private static void EnsureDraft(OrganizationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Status is not PlanStatus.Draft)
        {
            throw new InvalidOperationException("只有草稿整理计划可以编辑。");
        }
    }

    private static PlanningDecision CreatePlanningDecision(
        DesktopItem item,
        IReadOnlyList<OrganizationRule> rules,
        string managedDirectory,
        DateTimeOffset plannedAt)
    {
        var extension = System.IO.Path.GetExtension(item.Path);
        var matchingRules = rules
            .Where(rule => rule.IsEnabled)
            .OrderByDescending(rule => rule.Priority)
            .Where(rule => RuleMatches(rule, item, extension, plannedAt))
            .ToArray();

        if (matchingRules.Length == 0)
        {
            return new PlanningDecision(null, null);
        }

        var highestPriority = matchingRules[0].Priority;
        var choices = matchingRules
            .Where(rule => rule.Priority == highestPriority)
            .Select(rule => new RuleConflictChoice(
                rule.Id,
                rule.Name,
                SuggestedAction.Archive,
                System.IO.Path.Combine(
                    managedDirectory,
                    rule.RelativeDestination,
                    System.IO.Path.GetFileName(item.Path))))
            .ToArray();
        var distinctTargets = choices
            .Select(choice => choice.TargetPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctTargets.Length > 1)
        {
            return new PlanningDecision(
                null,
                new RuleConflict(
                    item.Id,
                    item.Path,
                    highestPriority,
                    choices,
                    item.Size,
                    item.ModifiedAt,
                    item.Kind));
        }

        var selectedChoice = choices[0];
        var explanation = string.Join(
            "、",
            choices.Select(choice => choice.RuleName).Distinct(StringComparer.OrdinalIgnoreCase));
        return new PlanningDecision(
            new PlanItem(
                item.Id,
                item.Path,
                selectedChoice.TargetPath,
                selectedChoice.SuggestedAction,
                explanation,
                item.Size,
                item.ModifiedAt,
                item.Kind),
            null);
    }

    private static bool RuleMatches(
        OrganizationRule rule,
        DesktopItem item,
        string extension,
        DateTimeOffset plannedAt)
    {
        if (rule.Extensions.Count > 0
            && !rule.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var itemKinds = rule.ItemKinds ?? [];
        if (itemKinds.Count > 0 && !itemKinds.Contains(item.Kind))
        {
            return false;
        }

        if (rule.MinimumSizeBytes is { } minimumSize && item.Size < minimumSize)
        {
            return false;
        }

        if (rule.MaximumSizeBytes is { } maximumSize && item.Size > maximumSize)
        {
            return false;
        }

        if (rule.ModifiedWithinDays is { } modifiedWithinDays
            && item.ModifiedAt < plannedAt.AddDays(-modifiedWithinDays))
        {
            return false;
        }

        var keywords = rule.FileNameKeywords ?? [];
        if (keywords.Count == 0)
        {
            return true;
        }

        var fileName = System.IO.Path.GetFileNameWithoutExtension(item.Path);
        return keywords.Any(keyword => fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record PlanningDecision(PlanItem? PlanItem, RuleConflict? Conflict);
}
