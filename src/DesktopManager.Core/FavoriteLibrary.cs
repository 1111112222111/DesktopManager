namespace DesktopManager.Core;

public sealed record FavoriteCollection(
    Guid Id,
    string Name,
    string[] ItemPaths);

public sealed class FavoriteLibrary
{
    public const int MaximumCollectionCount = 50;
    public const int MaximumNameLength = 40;

    private readonly FavoriteCollection[] _collections;

    public static FavoriteLibrary Empty { get; } = new([]);

    public FavoriteLibrary(IEnumerable<FavoriteCollection>? collections)
    {
        var normalized = new List<FavoriteCollection>();
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var collection in collections ?? [])
        {
            ArgumentNullException.ThrowIfNull(collection);
            if (collection.Id == Guid.Empty || !ids.Add(collection.Id))
            {
                throw new InvalidOperationException("收藏夹标识无效或重复。");
            }
            var name = NormalizeName(collection.Name);
            if (!names.Add(name))
            {
                throw new InvalidOperationException("收藏夹名称不能重复。");
            }
            var paths = (collection.ItemPaths ?? [])
                .Select(NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            normalized.Add(new FavoriteCollection(collection.Id, name, paths));
        }
        if (normalized.Count > MaximumCollectionCount)
        {
            throw new InvalidOperationException($"收藏夹最多 {MaximumCollectionCount} 个。");
        }
        _collections = normalized
            .OrderBy(collection => collection.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<FavoriteCollection> Collections => _collections;

    public FavoriteCollection Get(Guid collectionId) =>
        _collections.FirstOrDefault(collection => collection.Id == collectionId)
        ?? throw new InvalidOperationException("收藏夹不存在。");

    public FavoriteLibrary AddCollection(string name, out FavoriteCollection created)
    {
        if (_collections.Length >= MaximumCollectionCount)
        {
            throw new InvalidOperationException($"收藏夹最多 {MaximumCollectionCount} 个。");
        }
        var normalizedName = NormalizeName(name);
        EnsureUniqueName(normalizedName, exceptId: null);
        created = new FavoriteCollection(Guid.NewGuid(), normalizedName, []);
        return new FavoriteLibrary([.. _collections, created]);
    }

    public FavoriteLibrary Rename(Guid collectionId, string name)
    {
        _ = Get(collectionId);
        var normalizedName = NormalizeName(name);
        EnsureUniqueName(normalizedName, collectionId);
        return new FavoriteLibrary(_collections.Select(collection =>
            collection.Id == collectionId
                ? collection with { Name = normalizedName }
                : collection));
    }

    public FavoriteLibrary RemoveCollection(Guid collectionId)
    {
        _ = Get(collectionId);
        return new FavoriteLibrary(_collections.Where(collection => collection.Id != collectionId));
    }

    public FavoriteLibrary AddItem(Guid collectionId, string path)
    {
        var normalizedPath = NormalizePath(path);
        _ = Get(collectionId);
        return new FavoriteLibrary(_collections.Select(collection =>
            collection.Id == collectionId
                ? collection with { ItemPaths = [.. collection.ItemPaths, normalizedPath] }
                : collection));
    }

    public FavoriteLibrary RemoveItem(Guid collectionId, string path)
    {
        var normalizedPath = NormalizePath(path);
        _ = Get(collectionId);
        return new FavoriteLibrary(_collections.Select(collection =>
            collection.Id == collectionId
                ? collection with
                {
                    ItemPaths = collection.ItemPaths
                        .Where(itemPath => !string.Equals(
                            itemPath,
                            normalizedPath,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray()
                }
                : collection));
    }

    public FavoriteLibrary RebindItem(Guid collectionId, string oldPath, string newPath)
    {
        var normalizedOldPath = NormalizePath(oldPath);
        var normalizedNewPath = NormalizePath(newPath);
        var collection = Get(collectionId);
        if (!collection.ItemPaths.Contains(normalizedOldPath, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("要重新绑定的收藏夹成员不存在。");
        }

        return new FavoriteLibrary(_collections.Select(candidate =>
            candidate.Id == collectionId
                ? candidate with
                {
                    ItemPaths = [
                        .. candidate.ItemPaths.Where(path => !string.Equals(
                            path,
                            normalizedOldPath,
                            StringComparison.OrdinalIgnoreCase)),
                        normalizedNewPath]
                }
                : candidate));
    }

    public FavoriteLibrary RemoveItems(Guid collectionId, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var normalizedPaths = paths
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _ = Get(collectionId);
        return new FavoriteLibrary(_collections.Select(collection =>
            collection.Id == collectionId
                ? collection with
                {
                    ItemPaths = collection.ItemPaths
                        .Where(path => !normalizedPaths.Contains(path))
                        .ToArray()
                }
                : collection));
    }

    private void EnsureUniqueName(string name, Guid? exceptId)
    {
        if (_collections.Any(collection =>
                collection.Id != exceptId
                && string.Equals(collection.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("收藏夹名称不能重复。");
        }
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.Length > MaximumNameLength)
        {
            throw new InvalidOperationException($"收藏夹名称最多 {MaximumNameLength} 个字符。");
        }
        return normalized;
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }
}
