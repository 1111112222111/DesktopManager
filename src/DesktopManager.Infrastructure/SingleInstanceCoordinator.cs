namespace DesktopManager.Infrastructure;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly string _mutexName;
    private readonly EventWaitHandle _activationEvent;
    private readonly ManualResetEvent _stopEvent = new(false);
    private Mutex? _mutex;
    private Task? _activationListener;
    private bool _acquisitionAttempted;
    private bool _disposed;

    public SingleInstanceCoordinator(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        var safeId = string.Concat(applicationId.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-'
                ? character
                : '_'));
        _mutexName = $@"Local\{safeId}.SingleInstance";
        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            $@"Local\{safeId}.Activate");
    }

    public bool TryAcquire(Action activationRequested)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(activationRequested);
        if (_acquisitionAttempted)
        {
            throw new InvalidOperationException("单实例所有权只能申请一次。");
        }

        _acquisitionAttempted = true;
        _mutex = new Mutex(initiallyOwned: false, _mutexName, out var createdNew);
        if (!createdNew)
        {
            _activationEvent.Set();
            return false;
        }

        _activationListener = Task.Run(() => ListenForActivation(activationRequested));
        return true;
    }

    private void ListenForActivation(Action activationRequested)
    {
        var handles = new WaitHandle[] { _activationEvent, _stopEvent };
        while (WaitHandle.WaitAny(handles) == 0)
        {
            activationRequested();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopEvent.Set();
        _activationListener?.GetAwaiter().GetResult();
        _mutex?.Dispose();
        _activationEvent.Dispose();
        _stopEvent.Dispose();
    }
}
