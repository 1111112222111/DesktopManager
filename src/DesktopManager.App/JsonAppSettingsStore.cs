using System.IO;
using System.Text.Json;
using DesktopManager.Core;

namespace DesktopManager.App;

internal sealed record AppSettings(
    string? ManagedDirectory = null,
    string? MonitoredDirectory = null,
    bool IncludePublicDesktopReadOnly = true,
    OrganizationRule[]? Rules = null,
    NotificationPreferences? NotificationPreferences = null,
    DesktopItemPreference[]? ItemPreferences = null,
    GlobalHotKeyBinding? GlobalHotKeyBinding = null,
    FavoriteCollection[]? Favorites = null,
    CollectionWindowsPreferences? CollectionWindows = null,
    DesktopWidgetsPreferences? DesktopWidgets = null);

internal sealed class JsonAppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public JsonAppSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), SerializerOptions)
                ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(temporaryPath, _filePath, overwrite: true);
    }
}
