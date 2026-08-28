namespace DesktopManager.Core;

public static class CollectionItemTypeAhead
{
    public static int FindNextIndex(
        IReadOnlyList<string> names,
        int currentIndex,
        string prefix)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count == 0 || string.IsNullOrWhiteSpace(prefix))
        {
            return -1;
        }

        var start = currentIndex >= -1 && currentIndex < names.Count
            ? currentIndex + 1
            : 0;
        for (var offset = 0; offset < names.Count; offset++)
        {
            var index = (start + offset) % names.Count;
            if (names[index].StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
            {
                return index;
            }
        }
        return -1;
    }
}
