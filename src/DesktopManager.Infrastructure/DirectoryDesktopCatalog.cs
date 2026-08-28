using DesktopManager.Core;

namespace DesktopManager.Infrastructure;

public sealed class DirectoryDesktopCatalog
{
    private static readonly HashSet<string> IncompleteExtensions = new(
        [".crdownload", ".part", ".partial", ".tmp", ".temp", ".download"],
        StringComparer.OrdinalIgnoreCase);

    private readonly string _sourceDirectory;
    private readonly DesktopItemDispositionPolicy _dispositionPolicy;
    private readonly bool _isReadOnly;

    public DirectoryDesktopCatalog(
        string sourceDirectory,
        DesktopItemDispositionPolicy? dispositionPolicy = null,
        bool isReadOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        _sourceDirectory = Path.GetFullPath(sourceDirectory);
        _dispositionPolicy = dispositionPolicy ?? DesktopItemDispositionPolicy.Empty;
        _isReadOnly = isReadOnly;
    }

    public DesktopSnapshot GetSnapshot()
    {
        if (!Directory.Exists(_sourceDirectory))
        {
            return new DesktopSnapshot(_sourceDirectory, DateTimeOffset.UtcNow, []);
        }

        var items = Directory
            .EnumerateFileSystemEntries(_sourceDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(ShouldInclude)
            .Select(CreateDesktopItem)
            .ToArray();

        return new DesktopSnapshot(_sourceDirectory, DateTimeOffset.UtcNow, items);
    }

    public IDisposable ObserveChanges(Action<DesktopChange> onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);
        Directory.CreateDirectory(_sourceDirectory);

        var watcher = new FileSystemWatcher(_sourceDirectory)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
        };

        watcher.Created += (_, eventArgs) => NotifyIfIncluded(
            onChange,
            new DesktopChange(DesktopChangeKind.Created, Path.GetFullPath(eventArgs.FullPath)));
        watcher.Deleted += (_, eventArgs) =>
            NotifyIfIncluded(
                onChange,
                new DesktopChange(DesktopChangeKind.Deleted, Path.GetFullPath(eventArgs.FullPath)),
                attributesAvailable: false);
        watcher.Changed += (_, eventArgs) => NotifyIfIncluded(
            onChange,
            new DesktopChange(DesktopChangeKind.Changed, Path.GetFullPath(eventArgs.FullPath)));
        watcher.Renamed += (_, eventArgs) => NotifyIfIncluded(
            onChange,
            new DesktopChange(
                DesktopChangeKind.Renamed,
                Path.GetFullPath(eventArgs.FullPath),
                Path.GetFullPath(eventArgs.OldFullPath)));
        watcher.Error += (_, _) => onChange(new DesktopChange(
            DesktopChangeKind.Reset,
            _sourceDirectory));
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    public DesktopItem? GetItem(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(
                Path.GetDirectoryName(fullPath),
                _sourceDirectory,
                StringComparison.OrdinalIgnoreCase)
            || !Path.Exists(fullPath)
            || !ShouldInclude(fullPath))
        {
            return null;
        }
        return CreateDesktopItem(fullPath);
    }

    private void NotifyIfIncluded(
        Action<DesktopChange> onChange,
        DesktopChange change,
        bool attributesAvailable = true)
    {
        if (ShouldInclude(change.Path, attributesAvailable))
        {
            onChange(change);
        }
    }

    private bool ShouldInclude(string path) => ShouldInclude(path, attributesAvailable: true);

    private bool ShouldInclude(string path, bool attributesAvailable)
    {
        if (_dispositionPolicy.GetDisposition(path) is DesktopItemDisposition.Ignore)
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase)
            || IncompleteExtensions.Contains(Path.GetExtension(fileName)))
        {
            return false;
        }

        if (!attributesAvailable)
        {
            return true;
        }

        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private DesktopItem CreateDesktopItem(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            var directory = new DirectoryInfo(fullPath);
            return new DesktopItem(
                Guid.NewGuid(),
                DesktopItemKind.Folder,
                fullPath,
                0,
                directory.LastWriteTimeUtc,
                directory.CreationTimeUtc,
                _isReadOnly);
        }

        var file = new FileInfo(fullPath);
        var kind = file.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            ? DesktopItemKind.Shortcut
            : DesktopItemKind.File;
        return new DesktopItem(
            Guid.NewGuid(),
            kind,
            fullPath,
            file.Length,
            file.LastWriteTimeUtc,
            file.CreationTimeUtc,
            _isReadOnly);
    }
}
