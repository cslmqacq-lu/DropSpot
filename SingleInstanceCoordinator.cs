namespace DropSpot;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string DefaultInstanceKey = "DropSpot";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _cancellation = new();
    private Thread? _listenerThread;
    private bool _disposed;

    public SingleInstanceCoordinator(string? instanceKey = null)
    {
        var key = string.IsNullOrWhiteSpace(instanceKey) ? DefaultInstanceKey : instanceKey;
        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            $@"Local\{key}.Activate");
        _mutex = new Mutex(
            initiallyOwned: true,
            $@"Local\{key}.SingleInstance",
            out var createdNew);
        IsFirstInstance = createdNew;
    }

    public bool IsFirstInstance { get; }

    public void SignalActivation()
    {
        _activationEvent.Set();
    }

    public void StartListening(Action activationAction)
    {
        ArgumentNullException.ThrowIfNull(activationAction);
        if (!IsFirstInstance || _listenerThread is not null)
        {
            return;
        }

        _listenerThread = new Thread(() => Listen(activationAction))
        {
            IsBackground = true,
            Name = "DropSpot activation listener"
        };
        _listenerThread.Start();
    }

    private void Listen(Action activationAction)
    {
        var handles = new WaitHandle[] { _activationEvent, _cancellation.Token.WaitHandle };
        while (!_cancellation.IsCancellationRequested)
        {
            var signaled = WaitHandle.WaitAny(handles);
            if (signaled != 0 || _cancellation.IsCancellationRequested)
            {
                return;
            }

            try
            {
                activationAction();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                // The main window may be closing while another launch is signaling it.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        _listenerThread?.Join(TimeSpan.FromSeconds(1));
        if (IsFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex can already be abandoned during process shutdown.
            }
        }

        _mutex.Dispose();
        _activationEvent.Dispose();
        _cancellation.Dispose();
    }
}
