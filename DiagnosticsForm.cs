using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DropSpot;

public sealed class DiagnosticsForm : Form
{
    private readonly Func<DiagnosticsSnapshot> _snapshotProvider;
    private readonly TextBox _content = new();

    public DiagnosticsForm(Func<DiagnosticsSnapshot> snapshotProvider)
    {
        _snapshotProvider = snapshotProvider;
        Text = "DropSpot 诊断";
        Icon = AppIcon.Create();
        Size = new Size(680, 520);
        MinimumSize = new Size(520, 400);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Microsoft YaHei UI", 9F);
        BuildUi();
        RefreshSnapshot();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            Padding = new Padding(12),
            BackColor = Theme.Window
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(root);

        _content.Dock = DockStyle.Fill;
        _content.Multiline = true;
        _content.ReadOnly = true;
        _content.ScrollBars = ScrollBars.Both;
        _content.WordWrap = false;
        _content.BackColor = Theme.Panel;
        _content.ForeColor = Theme.Text;
        _content.BorderStyle = BorderStyle.FixedSingle;
        _content.Font = new Font("Consolas", 9F);
        root.Controls.Add(_content, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Window
        };
        root.Controls.Add(buttons, 0, 1);
        buttons.Controls.Add(CreateButton("关闭", (_, _) => Close()));
        buttons.Controls.Add(CreateButton("复制诊断", (_, _) => CopyDiagnostics()));
        buttons.Controls.Add(CreateButton("打开日志目录", (_, _) => OpenLogDirectory()));
        buttons.Controls.Add(CreateButton("刷新", (_, _) => RefreshSnapshot()));
    }

    private static Button CreateButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 28,
            Margin = new Padding(6, 7, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text
        };
        button.FlatAppearance.BorderColor = Theme.Border;
        button.Click += click;
        return button;
    }

    private void RefreshSnapshot()
    {
        _content.Text = _snapshotProvider().Format();
        _content.SelectionStart = 0;
        _content.SelectionLength = 0;
    }

    private void CopyDiagnostics()
    {
        try
        {
            Clipboard.SetText(_content.Text);
        }
        catch (ExternalException ex)
        {
            AppLog.Warning($"复制诊断失败：{ex.Message}");
        }
    }

    private static void OpenLogDirectory()
    {
        Directory.CreateDirectory(AppLog.LogDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", AppLog.LogDirectory) { UseShellExecute = true });
    }
}
