using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace DropSpot;

public enum MonitorAgentState
{
    /// <summary>界面本身就是管理员权限，直接在进程内监视。</summary>
    InProcess,
    /// <summary>尚未授权：没有后台监视任务，需要用户确认一次 UAC。</summary>
    NotAuthorized,
    /// <summary>后台监视任务指向的程序位置与当前程序不同，需要重新授权。</summary>
    Outdated,
    Starting,
    Connected,
    Disconnected,
    Stopped
}

/// <summary>
/// 界面使用的监视入口，API 与 <see cref="FileMonitorService"/> 保持一致：
///   - 界面是管理员权限时直接在进程内监视；
///   - 否则启动（或连接）后台监视进程，通过命名管道接收文件变化。
/// 事件可能在后台线程触发，与 FileMonitorService 相同。
/// </summary>
public sealed class MonitorHost : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    private readonly FileMonitorService? _inProcess;
    private readonly ConcurrentDictionary<string, VolumeMonitorStatus> _remoteStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly System.Threading.Timer? _watchdog;
    private StreamWriter? _writer;
    private Task? _serverLoop;
    private string[] _scopes = Array.Empty<string>();
    private string[] _excluded = Array.Empty<string>();
    private bool _filterNoise = true;
    private bool _running;
    private bool _disposed;
    private DateTime _lastLaunchAttempt = DateTime.MinValue;
    private int _launchFailures;
    private MonitorAgentState _state;

    public MonitorHost()
    {
        if (Elevation.IsElevated)
        {
            _inProcess = new FileMonitorService();
            _inProcess.Changed += (_, record) => Changed?.Invoke(this, record);
            _inProcess.MonitorError += (_, message) => MonitorError?.Invoke(this, message);
            _inProcess.VolumeStatusChanged += (_, status) => VolumeStatusChanged?.Invoke(this, status);
            _state = MonitorAgentState.InProcess;
            return;
        }

        _state = MonitorAgentState.Stopped;
        _watchdog = new System.Threading.Timer(_ => Watchdog(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public event EventHandler<ChangeRecord>? Changed;
    public event EventHandler<string>? MonitorError;
    public event EventHandler<VolumeMonitorStatus>? VolumeStatusChanged;
    public event EventHandler<MonitorAgentState>? AgentStateChanged;

    public MonitorAgentState AgentState => _state;

    public bool IsRunning => _inProcess?.IsRunning ?? _running;

    public IReadOnlyCollection<string> ActiveScopes => _inProcess?.ActiveScopes ?? (_running ? _scopes : Array.Empty<string>());

    public IReadOnlyList<VolumeMonitorStatus> VolumeStatuses
    {
        get
        {
            if (_inProcess is not null)
            {
                return _inProcess.VolumeStatuses;
            }

            if (!_running)
            {
                return Array.Empty<VolumeMonitorStatus>();
            }

            if (_state == MonitorAgentState.Connected && !_remoteStatuses.IsEmpty)
            {
                return _remoteStatuses.Values.OrderBy(item => item.VolumeRoot, StringComparer.OrdinalIgnoreCase).ToArray();
            }

            var message = _state switch
            {
                MonitorAgentState.NotAuthorized => "后台监视尚未授权",
                MonitorAgentState.Outdated => "需要重新授权后台监视",
                _ => "正在启动后台监视进程"
            };
            return _scopes
                .Select(scope => Path.GetPathRoot(scope) ?? scope)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(root => VolumeMonitorStatus.Waiting(root) with { Message = message })
                .ToArray();
        }
    }

    public bool FilterCommonNoise
    {
        get => _inProcess?.FilterCommonNoise ?? _filterNoise;
        set
        {
            _filterNoise = value;
            if (_inProcess is not null)
            {
                _inProcess.FilterCommonNoise = value;
                return;
            }

            if (_running)
            {
                Send(new AgentMessage { Type = AgentMessage.Exclusions, Excluded = _excluded.ToList(), FilterNoise = value });
            }
        }
    }

    public void Start(IEnumerable<WatchScope> scopes, IEnumerable<string>? excludedPaths = null)
    {
        var enabled = scopes.Where(scope => scope.Enabled).ToArray();
        _excluded = (excludedPaths ?? Array.Empty<string>()).ToArray();
        if (_inProcess is not null)
        {
            _inProcess.Start(enabled, _excluded);
            return;
        }

        _scopes = enabled.Select(scope => scope.Path).ToArray();
        _running = _scopes.Length > 0;
        _remoteStatuses.Clear();
        if (!_running)
        {
            return;
        }

        EnsureServer();
        if (_state == MonitorAgentState.Connected)
        {
            SendConfig();
        }
        else
        {
            LaunchAgent(force: true);
        }
    }

    public void Stop()
    {
        if (_inProcess is not null)
        {
            _inProcess.Stop();
            return;
        }

        _running = false;
        _remoteStatuses.Clear();
        Send(new AgentMessage { Type = AgentMessage.StopMonitor });
    }

    public void SetExcludedPaths(IEnumerable<string> excludedPaths)
    {
        _excluded = excludedPaths.ToArray();
        if (_inProcess is not null)
        {
            _inProcess.SetExcludedPaths(_excluded);
            return;
        }

        Send(new AgentMessage { Type = AgentMessage.Exclusions, Excluded = _excluded.ToList(), FilterNoise = _filterNoise });
    }

    /// <summary>
    /// 授权后台监视：以管理员身份启动一次监视进程（会弹出 UAC），它会注册免确认的计划任务并直接开始工作。
    /// </summary>
    public bool RequestAuthorization(out string? error)
    {
        if (_inProcess is not null)
        {
            error = null;
            return true;
        }

        EnsureServer();
        if (_state == MonitorAgentState.Connected || _state == MonitorAgentState.Outdated)
        {
            // 让旧的后台进程（可能指向旧位置）退出，由新授权的进程接替。
            Send(new AgentMessage { Type = AgentMessage.Exit });
        }

        if (!Elevation.TryStartElevated($"{MonitorAgent.AgentArgument} {MonitorAgent.InstallArgument}", out error))
        {
            return false;
        }

        _lastLaunchAttempt = DateTime.UtcNow;
        _launchFailures = 0;
        SetState(MonitorAgentState.Starting);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watchdog?.Dispose();
        if (_inProcess is not null)
        {
            _inProcess.Dispose();
            return;
        }

        Send(new AgentMessage { Type = AgentMessage.Exit });
        _cts.Cancel();
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }

        try
        {
            _serverLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
    }

    private void EnsureServer()
    {
        if (_serverLoop is null && !_disposed)
        {
            _serverLoop = Task.Run(() => ServerLoopAsync(_cts.Token));
        }
    }

    private async Task ServerLoopAsync(CancellationToken token)
    {
        var pipeName = MonitorAgent.PipeName;
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                await HandleConnectionAsync(server, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warning($"后台监视管道异常：{ex.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                lock (_sync)
                {
                    _writer = null;
                }

                server?.Dispose();
            }

            if (!token.IsCancellationRequested && _state is MonitorAgentState.Connected or MonitorAgentState.Starting)
            {
                _remoteStatuses.Clear();
                SetState(MonitorAgentState.Disconnected);
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken token)
    {
        using var reader = new StreamReader(server, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        var writer = new StreamWriter(server, Utf8, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        lock (_sync)
        {
            _writer = writer;
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                AgentMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<AgentMessage>(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (message is not null)
                {
                    HandleMessage(message);
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                _writer = null;
            }

            writer.Dispose();
        }
    }

    private void HandleMessage(AgentMessage message)
    {
        switch (message.Type)
        {
            case AgentMessage.Hello:
                var ownPath = Path.GetFullPath(MonitorAgent.ExecutablePath);
                var agentPath = string.IsNullOrWhiteSpace(message.ExePath) ? string.Empty : Path.GetFullPath(message.ExePath);
                if (!string.Equals(ownPath, agentPath, StringComparison.OrdinalIgnoreCase))
                {
                    AppLog.Warning($"后台监视任务指向其他位置（{agentPath}），需要重新授权");
                    Send(new AgentMessage { Type = AgentMessage.Exit });
                    SetState(MonitorAgentState.Outdated);
                    return;
                }

                AppLog.Info($"已连接后台监视进程 v{message.Version}");
                _launchFailures = 0;
                SetState(MonitorAgentState.Connected);
                if (_running)
                {
                    SendConfig();
                }
                else
                {
                    Send(new AgentMessage { Type = AgentMessage.StopMonitor });
                }

                break;
            case AgentMessage.Change:
                if (_running && message.ToRecord() is { } record)
                {
                    Changed?.Invoke(this, record);
                }

                break;
            case AgentMessage.Status:
                if (message.ToStatus() is { } status)
                {
                    _remoteStatuses[status.VolumeRoot] = status;
                    VolumeStatusChanged?.Invoke(this, status);
                }

                break;
            case AgentMessage.Error:
                if (!string.IsNullOrWhiteSpace(message.Message))
                {
                    MonitorError?.Invoke(this, message.Message);
                }

                break;
        }
    }

    private void SendConfig()
    {
        Send(new AgentMessage
        {
            Type = AgentMessage.Config,
            Scopes = _scopes.ToList(),
            Excluded = _excluded.ToList(),
            FilterNoise = _filterNoise
        });
    }

    private void Send(AgentMessage message)
    {
        lock (_sync)
        {
            if (_writer is null)
            {
                return;
            }

            try
            {
                _writer.WriteLine(JsonSerializer.Serialize(message));
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                _writer = null;
            }
        }
    }

    /// <summary>通过免确认的计划任务启动后台监视进程；没有任务时进入“未授权”状态。</summary>
    private void LaunchAgent(bool force)
    {
        if (_disposed || _inProcess is not null || !_running)
        {
            return;
        }

        if (_state is MonitorAgentState.Connected or MonitorAgentState.Outdated)
        {
            return;
        }

        var backoff = TimeSpan.FromSeconds(Math.Min(60, 10 * Math.Max(1, _launchFailures)));
        if (!force && DateTime.UtcNow - _lastLaunchAttempt < backoff)
        {
            return;
        }

        _lastLaunchAttempt = DateTime.UtcNow;
        Task.Run(() =>
        {
            if (!ScheduledTasks.Exists(MonitorAgent.TaskName))
            {
                SetState(MonitorAgentState.NotAuthorized);
                return;
            }

            if (ScheduledTasks.Start(MonitorAgent.TaskName, out var error))
            {
                SetState(MonitorAgentState.Starting);
            }
            else
            {
                _launchFailures++;
                AppLog.Warning($"启动后台监视任务失败：{error}");
                SetState(MonitorAgentState.Disconnected);
            }
        });
    }

    private void Watchdog()
    {
        if (_disposed || !_running)
        {
            return;
        }

        switch (_state)
        {
            case MonitorAgentState.Starting when DateTime.UtcNow - _lastLaunchAttempt > ConnectTimeout:
                _launchFailures++;
                SetState(MonitorAgentState.Disconnected);
                break;
            case MonitorAgentState.Disconnected:
            case MonitorAgentState.Stopped:
                LaunchAgent(force: false);
                break;
        }
    }

    private void SetState(MonitorAgentState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        AgentStateChanged?.Invoke(this, state);
    }
}
