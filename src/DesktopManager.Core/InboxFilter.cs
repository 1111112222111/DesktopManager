namespace DesktopManager.Core;

public enum InboxModifiedFilter
{
    Any,
    Last7Days,
    Last30Days,
    OlderThan30Days
}

public enum InboxSizeFilter
{
    Any,
    Under1Megabyte,
    From1To100Megabytes,
    AtLeast100Megabytes
}

public enum InboxCreatedFilter
{
    Any,
    AddedLast7Days,
    AddedLast30Days,
    OlderThan30Days
}

public sealed record InboxFilterCriteria(
    string SearchText,
    DesktopItemKind? Kind,
    InboxModifiedFilter Modified,
    InboxSizeFilter Size,
    InboxCreatedFilter Created = InboxCreatedFilter.Any)
{
    private const long OneMegabyte = 1024 * 1024;
    private const long OneHundredMegabytes = 100 * OneMegabyte;

    public bool Matches(DesktopItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);
        var searchText = SearchText?.Trim() ?? string.Empty;
        if (searchText.Length > 0
            && !Path.GetFileName(item.Path).Contains(searchText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (Kind is { } kind && item.Kind != kind)
        {
            return false;
        }
        if (Modified switch
            {
                InboxModifiedFilter.Last7Days => item.ModifiedAt < now.AddDays(-7),
                InboxModifiedFilter.Last30Days => item.ModifiedAt < now.AddDays(-30),
                InboxModifiedFilter.OlderThan30Days => item.ModifiedAt >= now.AddDays(-30),
                _ => false
            })
        {
            return false;
        }
        var createdAt = item.CreatedAt ?? item.ModifiedAt;
        if (Created switch
            {
                InboxCreatedFilter.AddedLast7Days => createdAt < now.AddDays(-7),
                InboxCreatedFilter.AddedLast30Days => createdAt < now.AddDays(-30),
                InboxCreatedFilter.OlderThan30Days => createdAt >= now.AddDays(-30),
                _ => false
            })
        {
            return false;
        }
        if (Size is not InboxSizeFilter.Any && item.Kind is DesktopItemKind.Folder)
        {
            return false;
        }
        return Size switch
        {
            InboxSizeFilter.Under1Megabyte => item.Size < OneMegabyte,
            InboxSizeFilter.From1To100Megabytes => item.Size is >= OneMegabyte and < OneHundredMegabytes,
            InboxSizeFilter.AtLeast100Megabytes => item.Size >= OneHundredMegabytes,
            _ => true
        };
    }
}
