namespace DesktopManager.Core.Tests;

public sealed class InboxFilterTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");

    [Fact]
    public void Matches_CombinesSearchKindTimeAndSize()
    {
        var item = Item("季度报告.docx", DesktopItemKind.File, 2 * 1024 * 1024, Now.AddDays(-3));
        var filter = new InboxFilterCriteria(
            "报告",
            DesktopItemKind.File,
            InboxModifiedFilter.Last7Days,
            InboxSizeFilter.From1To100Megabytes);

        Assert.True(filter.Matches(item, Now));
        Assert.False(filter.Matches(item with { Kind = DesktopItemKind.Folder }, Now));
        Assert.False(filter.Matches(item with { ModifiedAt = Now.AddDays(-8) }, Now));
        Assert.False(filter.Matches(item with { Size = 512 }, Now));
        Assert.False(filter.Matches(item with { Kind = DesktopItemKind.Folder }, Now));
    }

    [Fact]
    public void Matches_OlderThan30DaysUsesExclusiveBoundary()
    {
        var filter = new InboxFilterCriteria(
            string.Empty,
            null,
            InboxModifiedFilter.OlderThan30Days,
            InboxSizeFilter.Any);

        Assert.True(filter.Matches(Item("旧文件.txt", DesktopItemKind.File, 1, Now.AddDays(-31)), Now));
        Assert.False(filter.Matches(Item("边界.txt", DesktopItemKind.File, 1, Now.AddDays(-30)), Now));
    }

    [Fact]
    public void Matches_CreatedFilterDistinguishesRecentlyAddedFromOldUnprocessed()
    {
        var recent = Item("recent.txt", DesktopItemKind.File, 1, Now.AddDays(-20)) with
        {
            CreatedAt = Now.AddDays(-2)
        };
        var recentlyAdded = new InboxFilterCriteria(
            string.Empty, null, InboxModifiedFilter.Any, InboxSizeFilter.Any,
            InboxCreatedFilter.AddedLast7Days);
        var longUnprocessed = recentlyAdded with { Created = InboxCreatedFilter.OlderThan30Days };

        Assert.True(recentlyAdded.Matches(recent, Now));
        Assert.False(longUnprocessed.Matches(recent, Now));
        Assert.True(longUnprocessed.Matches(recent with { CreatedAt = Now.AddDays(-45) }, Now));
    }

    private static DesktopItem Item(
        string name,
        DesktopItemKind kind,
        long size,
        DateTimeOffset modifiedAt) => new(
            Guid.NewGuid(), kind, Path.GetFullPath(Path.Combine("C:\\Desktop", name)), size, modifiedAt);
}
