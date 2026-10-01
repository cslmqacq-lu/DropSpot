namespace DropSpot;

public sealed class TrayIconController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggleMonitoringItem;

    public TrayIconController(
        Action restoreMain,
        Action openFloating,
        Action toggleMonitoring,
        Action openHistory,
        Action openDiagnostics,
        Action exit,
        Action? restartElevated = null)
    {
        var menu = DarkMenuRenderer.CreateMenu();
        menu.Items.Add("展开面板", null, (_, _) => restoreMain());
        menu.Items.Add("显示悬浮舱", null, (_, _) => openFloating());
        menu.Items.Add(new ToolStripSeparator());
        _toggleMonitoringItem = new ToolStripMenuItem("暂停监视", null, (_, _) => toggleMonitoring());
        menu.Items.Add(_toggleMonitoringItem);
        menu.Items.Add("活动历史", null, (_, _) => openHistory());
        menu.Items.Add("诊断信息", null, (_, _) => openDiagnostics());
        if (restartElevated is not null)
        {
            menu.Items.Add("授权后台监视", null, (_, _) => restartElevated());
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出 DropSpot", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = AppIcon.Create(),
            Text = "DropSpot",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                restoreMain();
            }
        };
        if (restartElevated is not null)
        {
            _icon.BalloonTipClicked += (_, _) => restartElevated();
        }
    }

    public void ShowNotice(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = ToolTipIcon.Warning;
        _icon.ShowBalloonTip(8000);
    }

    public void UpdateMonitoring(bool monitoring, IReadOnlyList<VolumeMonitorStatus> statuses)
    {
        _toggleMonitoringItem.Text = monitoring ? "暂停监视" : "继续监视";
        var healthy = statuses.Count(status => status.State == VolumeMonitorState.Healthy);
        var attention = statuses.Count(status => status.State is VolumeMonitorState.Waiting or VolumeMonitorState.Reconnecting or VolumeMonitorState.Error);
        var text = monitoring
            ? attention > 0 ? $"DropSpot · {healthy} 正常 / {attention} 待恢复" : "DropSpot · 监视中"
            : "DropSpot · 已暂停";
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
