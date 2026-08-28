namespace DesktopManager.Core;

public enum DesktopItemDisposition
{
    Inbox,
    Keep,
    Ignore
}

public sealed record DesktopItemPreference(
    string Path,
    DesktopItemDisposition Disposition);

public sealed class DesktopItemDispositionPolicy
{
    private readonly Dictionary<string, DesktopItemDisposition> _dispositions;

    public static DesktopItemDispositionPolicy Empty { get; } = new([]);

    public DesktopItemDispositionPolicy(IEnumerable<DesktopItemPreference>? preferences)
    {
        _dispositions = new Dictionary<string, DesktopItemDisposition>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var preference in preferences ?? [])
        {
            var path = NormalizePath(preference.Path);
            if (preference.Disposition is DesktopItemDisposition.Inbox)
            {
                _dispositions.Remove(path);
            }
            else
            {
                _dispositions[path] = preference.Disposition;
            }
        }
    }

    public IReadOnlyList<DesktopItemPreference> Preferences => _dispositions
        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
        .Select(pair => new DesktopItemPreference(pair.Key, pair.Value))
        .ToArray();

    public DesktopItemDisposition GetDisposition(string path)
    {
        var normalizedPath = NormalizePath(path);
        return _dispositions.TryGetValue(normalizedPath, out var disposition)
            ? disposition
            : DesktopItemDisposition.Inbox;
    }

    public DesktopItemDispositionPolicy WithDisposition(
        string path,
        DesktopItemDisposition disposition)
    {
        var normalizedPath = NormalizePath(path);
        var preferences = Preferences
            .Where(preference => !string.Equals(
                preference.Path,
                normalizedPath,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (disposition is not DesktopItemDisposition.Inbox)
        {
            preferences.Add(new DesktopItemPreference(normalizedPath, disposition));
        }

        return new DesktopItemDispositionPolicy(preferences);
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return System.IO.Path.GetFullPath(path);
    }
}
