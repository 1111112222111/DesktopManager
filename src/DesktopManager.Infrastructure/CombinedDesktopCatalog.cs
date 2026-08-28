using DesktopManager.Core;

namespace DesktopManager.Infrastructure;

public sealed class CombinedDesktopCatalog
{
    private readonly DirectoryDesktopCatalog _primary;
    private readonly DirectoryDesktopCatalog? _readOnly;
    private readonly string _primaryDirectory;

    public CombinedDesktopCatalog(
        string primaryDirectory,
        DesktopItemDispositionPolicy? dispositionPolicy = null,
        string? readOnlyDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryDirectory);
        _primaryDirectory = Path.GetFullPath(primaryDirectory);
        _primary = new DirectoryDesktopCatalog(_primaryDirectory, dispositionPolicy);
        if (!string.IsNullOrWhiteSpace(readOnlyDirectory)
            && !string.Equals(
                _primaryDirectory,
                Path.GetFullPath(readOnlyDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            _readOnly = new DirectoryDesktopCatalog(
                readOnlyDirectory,
                DesktopItemDispositionPolicy.Empty,
                isReadOnly: true);
        }
    }

    public DesktopSnapshot GetSnapshot()
    {
        var primary = _primary.GetSnapshot();
        var readOnly = _readOnly?.GetSnapshot();
        return new DesktopSnapshot(
            _primaryDirectory,
            DateTimeOffset.UtcNow,
            readOnly is null ? primary.Items : [.. primary.Items, .. readOnly.Items]);
    }

    public IDisposable ObserveChanges(Action<DesktopChange> onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);
        var subscriptions = new List<IDisposable> { _primary.ObserveChanges(onChange) };
        if (_readOnly is not null)
        {
            subscriptions.Add(_readOnly.ObserveChanges(onChange));
        }
        return new CompositeSubscription(subscriptions);
    }

    public DesktopSnapshot ApplyChanges(
        DesktopSnapshot snapshot,
        IReadOnlyList<DesktopChange> changes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Any(change => change.Kind is DesktopChangeKind.Reset))
        {
            return GetSnapshot();
        }
        var items = snapshot.Items.ToDictionary(
            item => Path.GetFullPath(item.Path),
            StringComparer.OrdinalIgnoreCase);
        foreach (var change in changes)
        {
            if (!string.IsNullOrWhiteSpace(change.PreviousPath))
            {
                items.Remove(Path.GetFullPath(change.PreviousPath));
            }
            var path = Path.GetFullPath(change.Path);
            if (change.Kind is DesktopChangeKind.Deleted)
            {
                items.Remove(path);
                continue;
            }

            var refreshed = GetItem(path);
            if (refreshed is null)
            {
                items.Remove(path);
            }
            else
            {
                items[path] = refreshed;
            }
        }
        return new DesktopSnapshot(
            _primaryDirectory,
            DateTimeOffset.UtcNow,
            items.Values.ToArray());
    }

    private DesktopItem? GetItem(string path)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.Equals(parent, _primaryDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return _primary.GetItem(path);
        }
        return _readOnly?.GetItem(path);
    }

    private sealed class CompositeSubscription(IReadOnlyList<IDisposable> subscriptions) : IDisposable
    {
        public void Dispose()
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }
    }
}
