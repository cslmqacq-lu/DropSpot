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

    public static VolumeMonitorStatus Waiting(string volumeRoot) => new(
        volumeRoot,
        VolumeMonitorState.Waiting,
        "等待磁盘就绪",
        0,
        null,
        null,
        null);
}
