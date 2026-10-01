using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace DropSpot;

/// <summary>
/// 后台监视进程（以管理员权限运行，无界面）。
///
/// 为什么要拆：读取 USN 日志必须是管理员，但管理员窗口收不到资源管理器的拖放（Windows UIPI 限制）。
/// 所以界面始终以普通权限运行，只有这个进程以管理员权限读取磁盘日志，
/// 通过命名管道把文件变化实时发给界面。
///
/// 安全约束：
///   - 管道由界面（普通权限）创建，并且只允许当前用户连接；
///   - 本进程连接后校验管道对端就是同一个 DropSpot.exe，否则立即退出；
///   - 只接受“监视哪些盘 / 排除哪些路径”的配置，不执行任何文件操作。
/// </summary>
internal static class MonitorAgent
{
    public const string AgentArgument = "--monitor-agent";
    public const string InstallArgument = "--install";
    public const string TaskName = "DropSpot Monitor";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>当前登录会话 + 当前用户专用的管道名（管理员进程与普通进程得到的结果相同）。</summary>
    public static string PipeName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? Environment.UserName;
            return $"DropSpot.Monitor.{Process.GetCurrentProcess().SessionId}.{sid}";
        }
    }

    public static string ExecutablePath => Environment.ProcessPath ?? Application.ExecutablePath;

    /// <summary>注册“按需运行、最高权限”的后台监视任务，并清理旧版任务。需要管理员权限。</summary>
    public static bool InstallTask(out string? error)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var xml = ScheduledTasks.BuildElevatedTaskXml(
            ExecutablePath,
            identity.Name,
            AgentArgument,
            "DropSpot 后台监视进程（以最高权限读取磁盘 USN 日志，界面保持普通权限）",
            runAtLogon: false);
        if (!ScheduledTasks.Register(TaskName, xml, out error))
        {
            return false;
        }

        if (ScheduledTasks.Exists(StartupRegistration.LegacyTaskName))
        {
            _ = ScheduledTasks.Delete(StartupRegistration.LegacyTaskName, out _);
        }

        return true;
    }

    /// <summary>后台监视进程入口，返回进程退出码。</summary>
    public static int Run(bool install)
    {
        AppLog.FileName = "MonitorAgent.log";
        AppLog.Info($"后台监视进程启动（{(Elevation.IsElevated ? "管理员" : "普通")}权限）");
        if (!Elevation.IsElevated)
        {
            AppLog.Warning("后台监视进程需要管理员权限，已退出");
            return 2;
        }

        if (install)
        {
            if (InstallTask(out var installError))
            {
                AppLog.Info("已注册后台监视任务");
            }
            else
            {
                AppLog.Warning($"注册后台监视任务失败：{installError}");
            }
        }
        else if (ScheduledTasks.Exists(StartupRegistration.LegacyTaskName))
        {
            _ = ScheduledTasks.Delete(StartupRegistration.LegacyTaskName, out _);
        }

        // 同一时间只保留一个后台监视进程；重新授权时旧进程会收到退出指令，这里稍等它让出位置。
        using var mutex = new Mutex(false, @"Local\DropSpot.MonitorAgent");
        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            AppLog.Info("已有后台监视进程在运行");
            return 0;
        }

        try
        {
            RunSession();
        }
        catch (Exception ex)
        {
            AppLog.Error("后台监视进程异常退出", ex);
            return 1;
        }
        finally
        {
            mutex.ReleaseMutex();
        }

        AppLog.Info("后台监视进程退出");
        return 0;
    }

    private static void RunSession()
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            pipe.Connect(60_000);
        }
        catch (TimeoutException)
        {
            AppLog.Warning("60 秒内没有连上 DropSpot 界面，退出");
            return;
        }

        if (!IsTrustedServer(pipe, out var serverPath))
        {
            if (serverPath is not null
                && string.Equals(Path.GetFileName(serverPath), "DropSpot.exe", StringComparison.OrdinalIgnoreCase))
            {
                // 对端是另一个位置的 DropSpot（比如刚安装的新版）：只告诉它“任务指向这里”，
                // 让它提示重新授权；不接收任何指令、不发送任何文件活动。
                AppLog.Warning($"后台监视任务属于 {ExecutablePath}，界面来自 {serverPath}，需要重新授权");
                try
                {
                    using var notice = new StreamWriter(pipe, Utf8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
                    notice.WriteLine(JsonSerializer.Serialize(new AgentMessage
                    {
                        Type = AgentMessage.Hello,
                        ExePath = ExecutablePath,
                        Version = Application.ProductVersion
                    }));
                    pipe.WaitForPipeDrain();
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                }

                return;
            }

            AppLog.Warning("管道对端不是本机的 DropSpot 程序，已拒绝连接");
            return;
        }

        using var reader = new StreamReader(pipe, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, Utf8, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        var writeLock = new object();
        var connected = true;

        void Send(AgentMessage message)
        {
            if (!connected)
            {
                return;
            }

            try
            {
                var line = JsonSerializer.Serialize(message);
                lock (writeLock)
                {
                    writer.WriteLine(line);
                }
            }
            catch (IOException)
            {
                connected = false;
            }
            catch (ObjectDisposedException)
            {
                connected = false;
            }
        }

        using var monitor = new FileMonitorService();
        monitor.Changed += (_, record) => Send(AgentMessage.FromRecord(record));
        monitor.VolumeStatusChanged += (_, status) => Send(AgentMessage.FromStatus(status));
        monitor.MonitorError += (_, message) => Send(new AgentMessage { Type = AgentMessage.Error, Message = message });

        Send(new AgentMessage
        {
            Type = AgentMessage.Hello,
            ExePath = ExecutablePath,
            Version = Application.ProductVersion
        });

        while (connected)
        {
            string? line;
            try
            {
                line = reader.ReadLine();
            }
            catch (IOException)
            {
                break;
            }

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

            try
            {
                Handle(message);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                AppLog.Warning($"处理界面指令失败：{ex.Message}");
            }
        }

        connected = false;

        void Handle(AgentMessage? message)
        {
            if (message is null)
            {
                return;
            }

            switch (message.Type)
            {
                case AgentMessage.Config:
                    monitor.FilterCommonNoise = message.FilterNoise;
                    PathRules.ConfigureFileFilter(message.FilterDev, message.HiddenExtensions);
                    monitor.Start(
                        (message.Scopes ?? new List<string>()).Select(path => new WatchScope(path)),
                        message.Excluded ?? new List<string>());
                    break;
                case AgentMessage.Exclusions:
                    monitor.FilterCommonNoise = message.FilterNoise;
                    PathRules.ConfigureFileFilter(message.FilterDev, message.HiddenExtensions);
                    monitor.SetExcludedPaths(message.Excluded ?? new List<string>());
                    break;
                case AgentMessage.StopMonitor:
                    monitor.Stop();
                    break;
                case AgentMessage.Exit:
                    connected = false;
                    break;
            }
        }
    }

    /// <summary>校验管道服务端进程就是与本进程相同路径的 DropSpot.exe。</summary>
    private static bool IsTrustedServer(NamedPipeClientStream pipe, out string? serverPath)
    {
        serverPath = null;
        try
        {
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId))
            {
                return false;
            }

            using var process = Process.GetProcessById((int)processId);
            serverPath = process.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(serverPath)
                && string.Equals(Path.GetFullPath(serverPath), Path.GetFullPath(ExecutablePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLog.Warning($"校验管道对端失败：{ex.Message}");
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}

/// <summary>界面与后台监视进程之间的消息（每条一行 JSON）。</summary>
internal sealed class AgentMessage
{
    public const string Hello = "hello";
    public const string Change = "change";
    public const string Status = "status";
    public const string Error = "error";
    public const string Config = "config";
    public const string Exclusions = "exclusions";
    public const string StopMonitor = "stop";
    public const string Exit = "exit";

    public string Type { get; set; } = string.Empty;

    // hello
    public string? ExePath { get; set; }
    public string? Version { get; set; }

    // config / exclusions
    public List<string>? Scopes { get; set; }
    public List<string>? Excluded { get; set; }
    public bool FilterNoise { get; set; } = true;
    public bool FilterDev { get; set; } = true;
    public List<string>? HiddenExtensions { get; set; }

    // change
    public DateTime Time { get; set; }
    public string? ChangeKind { get; set; }
    public string? FolderPath { get; set; }
    public string? FilePath { get; set; }
    public string? ScopePath { get; set; }
    public string? FileName { get; set; }

    // status
    public string? VolumeRoot { get; set; }
    public VolumeMonitorState State { get; set; }
    public int RetryCount { get; set; }
    public DateTime? LastConnectedAt { get; set; }
    public DateTime? LastEventAt { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public bool AccessDenied { get; set; }

    // error / status
    public string? Message { get; set; }

    public static AgentMessage FromRecord(ChangeRecord record) => new()
    {
        Type = Change,
        Time = record.Time,
        ChangeKind = record.ChangeKind,
        FolderPath = record.FolderPath,
        FilePath = record.FilePath,
        ScopePath = record.ScopePath,
        FileName = record.FileName
    };

    public static AgentMessage FromStatus(VolumeMonitorStatus status) => new()
    {
        Type = Status,
        VolumeRoot = status.VolumeRoot,
        State = status.State,
        Message = status.Message,
        RetryCount = status.RetryCount,
        LastConnectedAt = status.LastConnectedAt,
        LastEventAt = status.LastEventAt,
        LastErrorAt = status.LastErrorAt,
        AccessDenied = status.AccessDenied
    };

    public ChangeRecord? ToRecord()
    {
        if (string.IsNullOrWhiteSpace(FilePath) || string.IsNullOrWhiteSpace(FolderPath))
        {
            return null;
        }

        return new ChangeRecord
        {
            Time = Time,
            ChangeKind = ChangeKind ?? "更新",
            FolderPath = FolderPath,
            FilePath = FilePath,
            ScopePath = ScopePath ?? string.Empty,
            FileName = FileName ?? Path.GetFileName(FilePath)
        };
    }

    public VolumeMonitorStatus? ToStatus()
    {
        if (string.IsNullOrWhiteSpace(VolumeRoot))
        {
            return null;
        }

        return new VolumeMonitorStatus(
            VolumeRoot,
            State,
            Message ?? string.Empty,
            RetryCount,
            LastConnectedAt,
            LastEventAt,
            LastErrorAt)
        {
            AccessDenied = AccessDenied
        };
    }
}
