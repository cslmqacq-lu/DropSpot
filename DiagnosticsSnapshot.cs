using System.Text;

namespace DropSpot;

public sealed record DiagnosticsSnapshot(
    string Version,
    DateTime GeneratedAt,
    IReadOnlyList<VolumeMonitorStatus> Volumes,
    IReadOnlyList<string> RecentLogEntries,
    string LogDirectory)
{
    public static DiagnosticsSnapshot Create(IEnumerable<VolumeMonitorStatus> volumes)
    {
        return new DiagnosticsSnapshot(
            Application.ProductVersion,
            DateTime.Now,
            volumes.OrderBy(item => item.VolumeRoot, StringComparer.OrdinalIgnoreCase).ToArray(),
            AppLog.RecentEntries.TakeLast(20).ToArray(),
            AppLog.LogDirectory);
    }

    public string Format()
    {
        var text = new StringBuilder();
        text.AppendLine($"DropSpot v{Version}");
        text.AppendLine($"诊断时间：{GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"系统：{Environment.OSVersion}");
        text.AppendLine($"进程：{Environment.ProcessId} / 64位：{Environment.Is64BitProcess}");
        text.AppendLine();
        text.AppendLine("监视卷：");
        if (Volumes.Count == 0)
        {
            text.AppendLine("  未启动监视");
        }

        foreach (var volume in Volumes)
        {
            text.AppendLine($"  {volume.VolumeRoot}  {StateText(volume.State)}  {volume.Message}");
            text.AppendLine($"    重试：{volume.RetryCount}  连接：{FormatTime(volume.LastConnectedAt)}  事件：{FormatTime(volume.LastEventAt)}  错误：{FormatTime(volume.LastErrorAt)}");
        }

        text.AppendLine();
        text.AppendLine($"日志目录：{LogDirectory}");
        text.AppendLine("最近日志：");
        if (RecentLogEntries.Count == 0)
        {
            text.AppendLine("  暂无");
        }
        else
        {
            foreach (var entry in RecentLogEntries)
            {
                text.AppendLine(entry);
            }
        }

        return text.ToString().TrimEnd();
    }

    public static string StateText(VolumeMonitorState state) => state switch
    {
        VolumeMonitorState.Waiting => "等待",
        VolumeMonitorState.Connecting => "连接中",
        VolumeMonitorState.Healthy => "正常",
        VolumeMonitorState.Reconnecting => "重连中",
        VolumeMonitorState.Error => "错误",
        VolumeMonitorState.Stopped => "已停止",
        _ => state.ToString()
    };

    private static string FormatTime(DateTime? value) => value?.ToString("MM-dd HH:mm:ss") ?? "--";
}
