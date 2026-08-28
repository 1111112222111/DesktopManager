using System.IO;

namespace DesktopManager.Core;

public sealed record BackupRestorePlan(
    OrganizationRule[] Rules,
    NotificationPreferences Notifications,
    GlobalHotKeyBinding GlobalHotKeyBinding,
    DesktopItemPreference[] ItemPreferences,
    FavoriteCollection[] Favorites,
    ScopedOrganizationOperation[] Operations,
    int SkippedItemPreferenceCount,
    int SkippedFavoriteMemberCount,
    int SkippedOperationCount,
    CollectionWindowsPreferences CollectionWindows);

public static class BackupRestorePlanner
{
    public static BackupRestorePlan Create(
        BackupPackage package,
        string demoSourceDirectory,
        string demoManagedDirectory,
        string realSourceDirectory,
        string realManagedDirectory)
    {
        ArgumentNullException.ThrowIfNull(package);
        var demoSource = NormalizeRoot(demoSourceDirectory);
        var demoManaged = NormalizeRoot(demoManagedDirectory);
        var realSource = NormalizeRoot(realSourceDirectory);
        var realManaged = NormalizeRoot(realManagedDirectory);
        var rules = package.Settings.Rules
            ?? throw new InvalidDataException("备份包规则集合为空。");
        var preferences = package.Settings.ItemPreferences
            ?? throw new InvalidDataException("备份包项目处置偏好集合为空。");
        var operations = package.Operations
            ?? throw new InvalidDataException("备份包操作历史集合为空。");

        foreach (var rule in rules)
        {
            ValidateRelativeDestination(rule.RelativeDestination, demoManaged);
            ValidateRelativeDestination(rule.RelativeDestination, realManaged);
        }

        var safePreferences = preferences
            .Where(preference => IsWithinAny(preference.Path, demoSource, realSource))
            .ToArray();
        var pathSafeOperations = operations
            .Where(scoped => IsOperationWithinScope(
                scoped,
                demoSource,
                demoManaged,
                realSource,
                realManaged))
            .ToArray();
        var safeOperationIds = pathSafeOperations
            .Select(scoped => (scoped.Scope, scoped.Operation.Id))
            .ToHashSet();
        var safeOperations = pathSafeOperations
            .Where(scoped => scoped.Operation.Kind is OperationKind.Organize
                || scoped.Operation.ReversesOperationId is { } originalId
                && safeOperationIds.Contains((scoped.Scope, originalId)))
            .ToArray();
        FavoriteLibrary favoriteLibrary;
        try
        {
            favoriteLibrary = new FavoriteLibrary(package.Settings.Favorites);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("备份包收藏夹内容无效。", exception);
        }
        var safeFavorites = favoriteLibrary.Collections
            .Select(collection => collection with
            {
                ItemPaths = collection.ItemPaths
                    .Where(path => IsWithinAny(path, demoSource, realSource))
                    .ToArray()
            })
            .ToArray();
        var favoriteMemberCount = favoriteLibrary.Collections.Sum(collection => collection.ItemPaths.Length);
        var safeFavoriteMemberCount = safeFavorites.Sum(collection => collection.ItemPaths.Length);
        var validZoneIds = CollectionZoneCatalog.Build(rules).Select(zone => zone.Id).ToHashSet();
        var collectionWindows = package.Settings.CollectionWindows ?? new CollectionWindowsPreferences();
        var safeLayouts = collectionWindows.EffectiveLayouts
            .Where(layout => validZoneIds.Contains(layout.ZoneId)
                && double.IsFinite(layout.Left)
                && double.IsFinite(layout.Top)
                && double.IsFinite(layout.Width)
                && double.IsFinite(layout.Height)
                && layout.Width > 0
                && layout.Height > 0)
            .ToArray();
        var safeItemOrders = collectionWindows.EffectiveItemOrders
            .Where(order => validZoneIds.Contains(order.ZoneId)
                && IsSafeRelativeDirectory(order.RelativeDirectory))
            .Select(order => order with
            {
                ItemNames = order.EffectiveItemNames
                    .Where(IsSafeItemName)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToArray()
            })
            .ToArray();

        return new BackupRestorePlan(
            rules,
            package.Settings.Notifications ?? NotificationPreferences.Default,
            GlobalHotKeyBinding.NormalizeOrDefault(package.Settings.GlobalHotKeyBinding),
            safePreferences,
            safeFavorites,
            safeOperations,
            preferences.Length - safePreferences.Length,
            favoriteMemberCount - safeFavoriteMemberCount,
            operations.Length - safeOperations.Length,
            collectionWindows with { Layouts = safeLayouts, ItemOrders = safeItemOrders });
    }

    private static bool IsSafeRelativeDirectory(string relativeDirectory)
    {
        if (string.IsNullOrWhiteSpace(relativeDirectory))
        {
            return true;
        }
        if (Path.IsPathRooted(relativeDirectory))
        {
            return false;
        }
        return relativeDirectory
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(segment => segment is not "." and not "..");
    }

    private static bool IsSafeItemName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name is not "." and not ".."
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !name.Contains(Path.DirectorySeparatorChar)
        && !name.Contains(Path.AltDirectorySeparatorChar);

    private static bool IsOperationWithinScope(
        ScopedOrganizationOperation scoped,
        string demoSource,
        string demoManaged,
        string realSource,
        string realManaged)
    {
        var (sourceRoot, targetRoot) = scoped.Scope switch
        {
            OperationScope.Demo => (demoSource, demoManaged),
            OperationScope.RealDesktop => (realSource, realManaged),
            _ => (string.Empty, string.Empty)
        };
        if (sourceRoot.Length == 0)
        {
            return false;
        }

        var operationSourceRoot = scoped.Operation.Kind is OperationKind.Undo
            ? targetRoot
            : sourceRoot;
        var operationTargetRoot = scoped.Operation.Kind is OperationKind.Undo
            ? sourceRoot
            : targetRoot;
        return scoped.Operation.Items.All(item =>
            IsWithin(item.SourcePath, operationSourceRoot)
            && IsWithin(item.TargetPath, operationTargetRoot));
    }

    private static bool IsWithinAny(string path, params string[] roots) =>
        roots.Any(root => IsWithin(path, root));

    private static bool IsWithin(string path, string root)
    {
        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string NormalizeRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void ValidateRelativeDestination(string destination, string managedRoot)
    {
        if (string.IsNullOrWhiteSpace(destination) || Path.IsPathRooted(destination))
        {
            throw new InvalidDataException("备份规则包含无效的归档目标。");
        }

        var resolved = Path.GetFullPath(destination, managedRoot);
        if (!IsWithin(resolved, managedRoot))
        {
            throw new InvalidDataException("备份规则的归档目标越过托管目录边界。");
        }
    }
}
