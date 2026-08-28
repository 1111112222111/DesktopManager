namespace DesktopManager.Core;

public enum PreflightIssueSeverity
{
    Warning,
    Blocking
}

public enum PreflightIssueKind
{
    PathOutsideAuthorizedRoots,
    SourceMissing,
    SourceBusy,
    RunningApplicationContained,
    ReparsePoint,
    TargetVolumeUnavailable,
    TargetDirectoryInaccessible,
    InsufficientSpace,
    ExistingTarget,
    DuplicateTarget,
    UnknownFolderSize,
    TargetDirectoryWillBeCreated,
    UnsupportedAction
}

public sealed record PreflightIssue(
    PreflightIssueKind Kind,
    PreflightIssueSeverity Severity,
    string Message,
    Guid? DesktopItemId = null);

public sealed record OrganizationPreflight(
    IReadOnlyList<PreflightIssue> Issues,
    long? AvailableSpaceBytes = null)
{
    public bool CanProceed => Issues.All(issue =>
        issue.Severity is not PreflightIssueSeverity.Blocking);

    public int BlockingCount => Issues.Count(issue =>
        issue.Severity is PreflightIssueSeverity.Blocking);

    public int WarningCount => Issues.Count(issue =>
        issue.Severity is PreflightIssueSeverity.Warning);
}
