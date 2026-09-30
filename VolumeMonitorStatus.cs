namespace DropSpot;

public enum VolumeMonitorState
{
    Waiting,
    Connecting,
    Healthy,
    Reconnecting,
    Error,
    Stopped
}

public sealed record VolumeMonitorStatus(
    string VolumeRoot,
    VolumeMonitorState State,
    string Message,
    int RetryCount,
    DateTime? LastConnectedAt,
    DateTime? LastEventAt,
    DateTime? LastErrorAt)
{
    public bool IsHealthy => State == VolumeMonitorState.Healthy;

    /// <summary>最近一次失败是因为权限不足（通常需要以管理员身份运行）。</summary>
    public bool AccessDenied { get; init; }

    public static VolumeMonitorStatus Waiting(string volumeRoot) => new(
        volumeRoot,
        VolumeMonitorState.Waiting,
        "等待磁盘就绪",
        0,
        null,
        null,
        null);
}
