namespace DesktopManager.Core;

public enum DesktopChangeKind
{
    Created,
    Deleted,
    Renamed,
    Changed,
    Reset
}

public sealed record DesktopChange(
    DesktopChangeKind Kind,
    string Path,
    string? PreviousPath = null);
