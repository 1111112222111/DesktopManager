namespace DesktopManager.Core;

public enum OperationStatus
{
    Running,
    PartiallyCompleted,
    Completed,
    Failed,
    Undone
}

public enum OperationKind
{
    Organize,
    Undo
}

public enum UndoConflictResolution
{
    Fail,
    SafeRename,
    Skip,
    AlternatePath
}

public sealed record UndoRequest(
    IReadOnlyList<string>? OriginalTargetPaths = null,
    UndoConflictResolution ConflictResolution = UndoConflictResolution.Fail,
    string? AlternateRestorePath = null);

public enum OperationItemStatus
{
    Pending,
    Succeeded,
    Skipped,
    Failed,
    Undone
}

public sealed record OperationItem(
    string SourcePath,
    string TargetPath,
    OperationItemStatus Status,
    string? Error);

public sealed record OrganizationOperation(
    Guid Id,
    Guid PlanId,
    OperationStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    OperationItem[] Items,
    OperationKind Kind = OperationKind.Organize,
    Guid? ReversesOperationId = null);
