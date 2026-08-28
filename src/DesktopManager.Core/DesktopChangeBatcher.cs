namespace DesktopManager.Core;

public sealed class DesktopChangeBatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeSpan _quietPeriod;
    private readonly Action<IReadOnlyList<DesktopChange>> _onBatch;
    private readonly Timer _timer;
    private readonly List<DesktopChange> _pendingChanges = [];
    private bool _disposed;

    public DesktopChangeBatcher(
        TimeSpan quietPeriod,
        Action<IReadOnlyList<DesktopChange>> onBatch)
    {
        if (quietPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(quietPeriod));
        }

        ArgumentNullException.ThrowIfNull(onBatch);
        _quietPeriod = quietPeriod;
        _onBatch = onBatch;
        _timer = new Timer(Flush, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Signal(DesktopChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pendingChanges.Add(change);
            _timer.Change(_quietPeriod, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pendingChanges.Clear();
            _timer.Dispose();
        }
    }

    private void Flush(object? state)
    {
        DesktopChange[] changes;
        lock (_gate)
        {
            if (_disposed || _pendingChanges.Count == 0)
            {
                return;
            }

            changes = [.. _pendingChanges];
            _pendingChanges.Clear();
        }

        _onBatch(changes);
    }
}
