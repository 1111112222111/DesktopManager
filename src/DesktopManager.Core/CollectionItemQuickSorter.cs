namespace DesktopManager.Core;

public enum CollectionItemSortMode
{
    Name,
    Size,
    Kind,
    ModifiedAt
}

public static class CollectionItemQuickSorter
{
    public static IReadOnlyList<T> Apply<T>(
        IReadOnlyList<T> items,
        CollectionItemSortMode mode,
        Func<T, string> nameSelector,
        Func<T, DesktopItemKind> kindSelector,
        Func<T, long> sizeSelector,
        Func<T, DateTimeOffset> modifiedAtSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(nameSelector);
        ArgumentNullException.ThrowIfNull(kindSelector);
        ArgumentNullException.ThrowIfNull(sizeSelector);
        ArgumentNullException.ThrowIfNull(modifiedAtSelector);

        IOrderedEnumerable<T> ordered = mode switch
        {
            CollectionItemSortMode.Size => items
                .OrderBy(item => kindSelector(item) is DesktopItemKind.Folder ? 0 : 1)
                .ThenByDescending(sizeSelector),
            CollectionItemSortMode.Kind => items
                .OrderBy(item => ExtensionGroupRank(kindSelector(item), nameSelector(item)))
                .ThenBy(
                    item => Path.GetExtension(nameSelector(item)).TrimStart('.'),
                    StringComparer.CurrentCultureIgnoreCase),
            CollectionItemSortMode.ModifiedAt => items
                .OrderBy(item => kindSelector(item) is DesktopItemKind.Folder ? 0 : 1)
                .ThenByDescending(modifiedAtSelector),
            _ => items
                .OrderBy(item => kindSelector(item) is DesktopItemKind.Folder ? 0 : 1)
                .ThenBy(nameSelector, StringComparer.CurrentCultureIgnoreCase)
        };

        return ordered
            .ThenBy(nameSelector, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static int ExtensionGroupRank(DesktopItemKind kind, string name)
    {
        if (kind is DesktopItemKind.Folder)
        {
            return 0;
        }
        return string.IsNullOrEmpty(Path.GetExtension(name)) ? 2 : 1;
    }
}
