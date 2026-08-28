namespace DesktopManager.Core;

public sealed record PlanTargetDistribution(
    string TargetDirectory,
    int ItemCount,
    long KnownSizeBytes,
    int UnknownSizeItemCount);

public sealed record OrganizationPlanSummary(
    int ExecutableItemCount,
    int ExcludedItemCount,
    int ConflictCount,
    long KnownTotalSizeBytes,
    int UnknownSizeItemCount,
    int DuplicateTargetCount,
    IReadOnlyList<PlanTargetDistribution> TargetDistribution);

public static class OrganizationPlanAnalyzer
{
    public static OrganizationPlanSummary Summarize(OrganizationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var targetDistribution = plan.Items
            .GroupBy(
                item => Path.GetDirectoryName(Path.GetFullPath(item.TargetPath)) ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new PlanTargetDistribution(
                group.Key,
                group.Count(),
                SumKnownSizes(group),
                group.Count(item => item.ObservedKind is DesktopItemKind.Folder)))
            .OrderByDescending(group => group.ItemCount)
            .ThenBy(group => group.TargetDirectory, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var duplicateTargetCount = plan.Items
            .GroupBy(item => Path.GetFullPath(item.TargetPath), StringComparer.OrdinalIgnoreCase)
            .Sum(group => Math.Max(0, group.Count() - 1));

        return new OrganizationPlanSummary(
            plan.Items.Count,
            plan.ExcludedItemIds.Count,
            plan.Conflicts.Count,
            SumKnownSizes(plan.Items),
            plan.Items.Count(item => item.ObservedKind is DesktopItemKind.Folder),
            duplicateTargetCount,
            targetDistribution);
    }

    private static long SumKnownSizes(IEnumerable<PlanItem> items)
    {
        var total = 0L;
        foreach (var size in items
                     .Where(item => item.ObservedKind is not DesktopItemKind.Folder)
                     .Select(item => Math.Max(0, item.ObservedSize)))
        {
            total = size > long.MaxValue - total ? long.MaxValue : total + size;
        }
        return total;
    }
}
