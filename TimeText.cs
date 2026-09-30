namespace DropSpot;

/// <summary>统一的时间显示：历史会跨天保存，超过当天的时间必须带上“昨天”或日期。</summary>
internal static class TimeText
{
    /// <summary>文件夹卡片用：刚刚 / N 分钟 / 14:20 / 昨天 14:20 / 09-28 14:20 / 2025-12-31。</summary>
    public static string Relative(DateTime time) => Relative(time, DateTime.Now);

    internal static string Relative(DateTime time, DateTime now)
    {
        var span = now - time;
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalSeconds < 60)
        {
            return "刚刚";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{Math.Max(1, (int)span.TotalMinutes)} 分钟";
        }

        return Clock(time, now);
    }

    /// <summary>文件行用：14:20 / 昨天 14:20 / 09-28 14:20 / 2025-12-31。</summary>
    public static string Clock(DateTime time) => Clock(time, DateTime.Now);

    internal static string Clock(DateTime time, DateTime now)
    {
        if (time.Date == now.Date)
        {
            return time.ToString("HH:mm");
        }

        if (time.Date == now.Date.AddDays(-1))
        {
            return $"昨天 {time:HH:mm}";
        }

        return time.Year == now.Year
            ? time.ToString("MM-dd HH:mm")
            : time.ToString("yyyy-MM-dd");
    }
}
