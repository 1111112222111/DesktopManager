using DesktopManager.Core;

namespace DesktopManager.Core.Tests;

public sealed class CollectionItemQuickSorterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Apply_ByName_KeepsFoldersFirstAndSortsIgnoringCase()
    {
        Item[] items =
        [
            new("beta.txt", DesktopItemKind.File, 10, Now),
            new("Folder", DesktopItemKind.Folder, 0, Now),
            new("Alpha.txt", DesktopItemKind.File, 20, Now)
        ];

        var result = Sort(items, CollectionItemSortMode.Name);

        Assert.Equal(["Folder", "Alpha.txt", "beta.txt"], result.Select(item => item.Name));
    }

    [Fact]
    public void Apply_BySize_UsesLargestFileFirstAfterFolders()
    {
        Item[] items =
        [
            new("small.txt", DesktopItemKind.File, 10, Now),
            new("Folder", DesktopItemKind.Folder, 0, Now),
            new("large.txt", DesktopItemKind.File, 50, Now)
        ];

        var result = Sort(items, CollectionItemSortMode.Size);

        Assert.Equal(["Folder", "large.txt", "small.txt"], result.Select(item => item.Name));
    }

    [Fact]
    public void Apply_ByKind_GroupsByExtensionAlphabeticallyThenSortsNames()
    {
        Item[] items =
        [
            new("zeta.DOCX", DesktopItemKind.File, 0, Now),
            new("notes.txt", DesktopItemKind.File, 0, Now),
            new("Photo.jpg", DesktopItemKind.File, 0, Now),
            new("alpha.docx", DesktopItemKind.File, 0, Now),
            new("App.lnk", DesktopItemKind.Shortcut, 0, Now),
            new("README", DesktopItemKind.File, 0, Now),
            new("Folder", DesktopItemKind.Folder, 0, Now)
        ];

        var result = Sort(items, CollectionItemSortMode.Kind);

        Assert.Equal(
            ["Folder", "alpha.docx", "zeta.DOCX", "Photo.jpg", "App.lnk", "notes.txt", "README"],
            result.Select(item => item.Name));
    }

    [Fact]
    public void Apply_ByModifiedAt_UsesNewestFileFirstAfterFolders()
    {
        Item[] items =
        [
            new("old.txt", DesktopItemKind.File, 0, Now.AddDays(-2)),
            new("new.txt", DesktopItemKind.File, 0, Now),
            new("Folder", DesktopItemKind.Folder, 0, Now.AddDays(-5))
        ];

        var result = Sort(items, CollectionItemSortMode.ModifiedAt);

        Assert.Equal(["Folder", "new.txt", "old.txt"], result.Select(item => item.Name));
    }

    private static IReadOnlyList<Item> Sort(Item[] items, CollectionItemSortMode mode) =>
        CollectionItemQuickSorter.Apply(
            items,
            mode,
            item => item.Name,
            item => item.Kind,
            item => item.Size,
            item => item.ModifiedAt);

    private sealed record Item(
        string Name,
        DesktopItemKind Kind,
        long Size,
        DateTimeOffset ModifiedAt);
}
