namespace DropSpot;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string DefaultInstanceKey = "DropSpot";
    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _activationEvent;
    private readonly CancellationTokenSource _cancellation = new();
    private Thread? _listenerThread;
    private bool _disposed;

    public SingleInstanceCoordinator(string? instanceKey = null)
    {
        var key = string.IsNullOrWhiteSpace(instanceKey) ? DefaultInstanceKey : instanceKey;
        try
        {
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
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // 已有一个以管理员身份运行的实例：普通权限进程无法打开它创建的同步对象。
            _activationEvent?.Dispose();
            _activationEvent = null;
            _mutex = null;
            IsFirstInstance = false;
            OtherInstanceIsElevated = true;
        }
    }

    public bool IsFirstInstance { get; }

    /// <summary>另一个实例以更高权限运行，当前进程无法唤醒它。</summary>
    public bool OtherInstanceIsElevated { get; }

    public void SignalActivation()
    {
        try
        {
            _activationEvent?.Set();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ObjectDisposedException)
        {
            // 无法唤醒更高权限的实例时静默忽略。
        }
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
        if (_activationEvent is null)
        {
            return;
        }

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
        if (IsFirstInstance && _mutex is not null)
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

        _mutex?.Dispose();
        _activationEvent?.Dispose();
        _cancellation.Dispose();
    }
}
