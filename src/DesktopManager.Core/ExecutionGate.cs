namespace DesktopManager.Core;

public sealed record ExecutionReview(
    OrganizationPlan Plan,
    PlanValidation Validation,
    OrganizationPlanSummary Summary,
    bool CanExecute,
    string RiskSummary);

public static class ExecutionGate
{
    public static ExecutionReview Review(
        OrganizationPlan plan,
        DesktopSnapshot currentSnapshot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(currentSnapshot);

        var validation = OrganizationPlanner.Validate(plan, currentSnapshot);
        var summary = OrganizationPlanAnalyzer.Summarize(plan);
        var itemCount = summary.ExecutableItemCount;
        var conflictCount = summary.ConflictCount;
        var canExecute = plan.Status is PlanStatus.Draft
            && itemCount > 0
            && conflictCount == 0
            && validation.IsValid;
        var sizeSummary = FormatSize(summary.KnownTotalSizeBytes)
            + (summary.UnknownSizeItemCount > 0
                ? $"，另有 {summary.UnknownSizeItemCount} 个文件夹大小未知"
                : string.Empty);
        var riskSummary = conflictCount == 0
            ? $"将移动 {itemCount} 个桌面项目（已知大小 {sizeSummary}），分布到 {summary.TargetDistribution.Count} 个目标目录；目标重名时会安全重命名，不会覆盖已有文件。"
            : $"检测到 {conflictCount} 个规则冲突；当前计划不能执行。请先逐项裁决或修改规则后重新生成计划。";
        if (summary.ExcludedItemCount > 0)
        {
            riskSummary += $" 当前草稿另有 {summary.ExcludedItemCount} 项已排除。";
        }
        if (summary.DuplicateTargetCount > 0)
        {
            riskSummary += $" 有 {summary.DuplicateTargetCount} 项与其他计划项目标同名，执行时将安全改名。";
        }

        return new ExecutionReview(
            plan,
            validation,
            summary,
            canExecute,
            riskSummary);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        _ => $"{bytes / (1024d * 1024 * 1024):0.#} GB"
    };

    public static OrganizationPlan PrepareForExecution(ExecutionReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!review.CanExecute)
        {
            throw new InvalidOperationException("整理计划为空、存在冲突、已过期或当前状态不允许执行。");
        }

        return review.Plan with { Status = PlanStatus.Confirmed };
    }
}
