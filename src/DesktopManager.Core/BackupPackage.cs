namespace DesktopManager.Core;

public static class BackupPackageFormat
{
    public const int CurrentVersion = 1;
}

public sealed record BackupManifest(
    int FormatVersion,
    DateTimeOffset CreatedAtUtc,
    string AppVersion);

public sealed record BackupSettings(
    string? ManagedDirectory,
    OrganizationRule[] Rules,
    NotificationPreferences Notifications,
    DesktopItemPreference[] ItemPreferences,
    GlobalHotKeyBinding? GlobalHotKeyBinding = null,
    FavoriteCollection[]? Favorites = null,
    CollectionWindowsPreferences? CollectionWindows = null);

public sealed record BackupPackage(
    BackupManifest Manifest,
    BackupSettings Settings,
    ScopedOrganizationOperation[] Operations);
