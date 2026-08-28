namespace DesktopManager.Core.Tests;

public sealed class FavoriteLibraryTests
{
    [Fact]
    public void AddItem_AllowsOnePathInMultipleCollectionsAndIsIdempotent()
    {
        var path = Path.GetFullPath("C:\\Desktop\\brief.docx");
        var library = FavoriteLibrary.Empty
            .AddCollection("本周", out var thisWeek)
            .AddCollection("客户", out var clients)
            .AddItem(thisWeek.Id, path)
            .AddItem(thisWeek.Id, path)
            .AddItem(clients.Id, path);

        Assert.Single(library.Get(thisWeek.Id).ItemPaths);
        Assert.Single(library.Get(clients.Id).ItemPaths);
    }

    [Fact]
    public void Rename_RejectsDuplicateNameIgnoringCase()
    {
        var library = FavoriteLibrary.Empty
            .AddCollection("本周", out _)
            .AddCollection("客户", out var clients);

        Assert.Throws<InvalidOperationException>(() => library.Rename(clients.Id, "本周"));
    }

    [Fact]
    public void RemoveItem_DoesNotRemoveCollectionOrOtherMemberships()
    {
        var path = Path.GetFullPath("C:\\Desktop\\brief.docx");
        var library = FavoriteLibrary.Empty
            .AddCollection("本周", out var thisWeek)
            .AddCollection("客户", out var clients)
            .AddItem(thisWeek.Id, path)
            .AddItem(clients.Id, path)
            .RemoveItem(thisWeek.Id, path);

        Assert.Empty(library.Get(thisWeek.Id).ItemPaths);
        Assert.Single(library.Get(clients.Id).ItemPaths);
    }

    [Fact]
    public void RebindItem_ChangesOnlySelectedCollectionAndMergesDuplicateTarget()
    {
        var oldPath = Path.GetFullPath("C:\\Desktop\\old.docx");
        var newPath = Path.GetFullPath("C:\\Desktop\\new.docx");
        var library = FavoriteLibrary.Empty
            .AddCollection("本周", out var thisWeek)
            .AddCollection("客户", out var clients)
            .AddItem(thisWeek.Id, oldPath)
            .AddItem(thisWeek.Id, newPath)
            .AddItem(clients.Id, oldPath)
            .RebindItem(thisWeek.Id, oldPath, newPath);

        Assert.Equal([newPath], library.Get(thisWeek.Id).ItemPaths);
        Assert.Equal([oldPath], library.Get(clients.Id).ItemPaths);
    }

    [Fact]
    public void RemoveItems_RemovesOnlyRequestedRelationships()
    {
        var missingA = Path.GetFullPath("C:\\Desktop\\a.docx");
        var missingB = Path.GetFullPath("C:\\Desktop\\b.docx");
        var existing = Path.GetFullPath("C:\\Desktop\\existing.docx");
        var library = FavoriteLibrary.Empty
            .AddCollection("本周", out var favorite)
            .AddItem(favorite.Id, missingA)
            .AddItem(favorite.Id, missingB)
            .AddItem(favorite.Id, existing)
            .RemoveItems(favorite.Id, [missingA, missingB]);

        Assert.Equal([existing], library.Get(favorite.Id).ItemPaths);
    }
}
