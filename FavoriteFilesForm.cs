using System.Runtime.InteropServices;

namespace DropSpot;

/// <summary>
/// 浮窗旁常驻的“收藏文件”小窗口。只要有收藏文件就跟随浮窗显示：
/// 单击打开、按住拖出真实文件、右键更多操作；也可以直接把文件拖进来收藏。
/// </summary>
internal sealed class FavoriteFilesForm : Form
{
    private const int FormWidth = 228;
    private const int HeaderHeight = 30;
    private const int RowHeight = 28;
    private const int FooterHeight = 24;
    private const int MaxVisibleRows = 8;
    private const int Gap = 8;

    private readonly Action<FavoriteFile, FileCommand> _command;
    private readonly Action _showAll;
    private readonly Label _header = new();
    private readonly Label _footer = new();
    private readonly FileRow[] _rows = new FileRow[MaxVisibleRows];
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolTip _toolTip = new();
    private readonly Font _headerFont = new("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
    private readonly Font _rowFont = new("Microsoft YaHei UI", 8.5F);
    private readonly Font _footerFont = new("Microsoft YaHei UI", 8F);
    private FavoriteFile? _menuTarget;

    public FavoriteFilesForm(Action<FavoriteFile, FileCommand> command, Action showAll)
    {
        _command = command;
        _showAll = showAll;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        DoubleBuffered = true;
        Width = FormWidth;
        Text = "DropSpot 收藏文件";

        BuildUi();
        BuildMenu();
        ShellFileDrop.EnableOleDrop(this, paths => BeginInvoke(() => PathsDropped?.Invoke(paths)));
    }

    /// <summary>有文件或文件夹拖到这个窗口上。</summary>
    public event Action<string[]>? PathsDropped;

    internal int DisplayedCount => _rows.Count(row => row.File is not null);

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExNoActivate = 0x08000000;
            const int wsExToolWindow = 0x00000080;
            var parameters = base.CreateParams;
            parameters.ExStyle |= wsExNoActivate | wsExToolWindow;
            return parameters;
        }
    }

    public void UpdateFiles(IReadOnlyList<FavoriteFile> files)
    {
        var visible = files.Take(MaxVisibleRows).ToArray();
        for (var index = 0; index < _rows.Length; index++)
        {
            _rows[index].Update(index < visible.Length ? visible[index] : null, _toolTip);
        }

        _header.Text = $"★ 收藏文件  {files.Count}";
        var hidden = files.Count - visible.Length;
        _footer.Visible = hidden > 0;
        _footer.Text = hidden > 0 ? $"还有 {hidden} 个，在主窗口查看" : string.Empty;
        Height = HeaderHeight + visible.Length * RowHeight + (hidden > 0 ? FooterHeight : 6);
        _footer.Top = HeaderHeight + visible.Length * RowHeight;
        Invalidate();
    }

    /// <summary>贴在浮窗左侧（左侧放不下时放右侧），底边对齐。</summary>
    public void PlaceBeside(Rectangle anchor)
    {
        var area = Screen.FromRectangle(anchor).WorkingArea;
        var x = anchor.Left - Width - Gap;
        if (x < area.Left)
        {
            x = anchor.Right + Gap;
        }

        var y = anchor.Bottom - Height;
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - Width));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - Height));
        Location = new Point(x, y);
    }

    protected override void WndProc(ref Message m)
    {
        if (ShellFileDrop.TryRead(ref m, out var paths))
        {
            if (paths.Length > 0)
            {
                PathsDropped?.Invoke(paths);
            }

            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ShellFileDrop.Enable(this);
        var roundCorners = 2; // DWMWCP_ROUND，Windows 11 圆角
        _ = DwmSetWindowAttribute(Handle, 33, ref roundCorners, sizeof(int));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Theme.BorderStrong);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using var divider = new Pen(Theme.Border);
        e.Graphics.DrawLine(divider, 8, HeaderHeight - 1, Width - 9, HeaderHeight - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _menu.Dispose();
            _toolTip.Dispose();
            _headerFont.Dispose();
            _rowFont.Dispose();
            _footerFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        _header.Font = _headerFont;
        _header.ForeColor = Color.FromArgb(242, 196, 92);
        _header.BackColor = Theme.Panel;
        _header.TextAlign = ContentAlignment.MiddleLeft;
        _header.Bounds = new Rectangle(10, 2, FormWidth - 20, HeaderHeight - 4);
        Controls.Add(_header);
        _toolTip.SetToolTip(_header, "把文件拖到浮窗或这里即可收藏；收藏的文件会归到所在的收藏文件夹下");

        for (var index = 0; index < _rows.Length; index++)
        {
            var row = new FileRow(this, _rowFont)
            {
                Top = HeaderHeight + index * RowHeight
            };
            _rows[index] = row;
            Controls.Add(row.Panel);
        }

        _footer.Font = _footerFont;
        _footer.ForeColor = Theme.Muted;
        _footer.BackColor = Theme.Panel;
        _footer.TextAlign = ContentAlignment.MiddleCenter;
        _footer.Bounds = new Rectangle(1, HeaderHeight, FormWidth - 2, FooterHeight - 2);
        _footer.Cursor = Cursors.Hand;
        _footer.Visible = false;
        _footer.Click += (_, _) => _showAll();
        Controls.Add(_footer);
    }

    private void BuildMenu()
    {
        _menu.BackColor = Theme.Panel;
        _menu.ForeColor = Theme.Text;
        _menu.ShowImageMargin = false;
        AddMenuItem("打开", FileCommand.Open);
        AddMenuItem("在文件夹中显示", FileCommand.Reveal);
        _menu.Items.Add(new ToolStripSeparator());
        AddMenuItem("复制文件", FileCommand.CopyFile);
        AddMenuItem("复制文件路径", FileCommand.CopyPath);
        _menu.Items.Add(new ToolStripSeparator());
        AddMenuItem("取消收藏", FileCommand.ToggleFavorite);
    }

    private void AddMenuItem(string text, FileCommand command)
    {
        _menu.Items.Add(text, null, (_, _) =>
        {
            if (_menuTarget is not null)
            {
                _command(_menuTarget, command);
            }
        });
    }

    private sealed class FileRow
    {
        private readonly FavoriteFilesForm _owner;
        private readonly PictureBox _icon = new();
        private readonly Label _name = new();
        private string? _iconPath;
        private Point _mouseDownPosition;
        private bool _dragCandidate;

        public FileRow(FavoriteFilesForm owner, Font font)
        {
            _owner = owner;
            Panel = new Panel
            {
                Left = 1,
                Width = FormWidth - 2,
                Height = RowHeight,
                BackColor = Theme.Panel,
                Cursor = Cursors.Hand,
                Visible = false
            };

            _icon.SizeMode = PictureBoxSizeMode.CenterImage;
            _icon.Bounds = new Rectangle(10, 5, 18, 18);
            _icon.BackColor = Theme.Panel;
            Panel.Controls.Add(_icon);

            _name.AutoEllipsis = true;
            _name.Font = font;
            _name.ForeColor = Theme.Text;
            _name.BackColor = Theme.Panel;
            _name.TextAlign = ContentAlignment.MiddleLeft;
            _name.Bounds = new Rectangle(34, 3, FormWidth - 46, RowHeight - 6);
            Panel.Controls.Add(_name);

            foreach (var control in new Control[] { Panel, _icon, _name })
            {
                control.Cursor = Cursors.Hand;
                control.ContextMenuStrip = owner._menu;
                control.MouseDown += HandleMouseDown;
                control.MouseMove += HandleMouseMove;
                control.MouseUp += HandleMouseUp;
                control.MouseEnter += (_, _) => SetHover(true);
                control.MouseLeave += (_, _) =>
                {
                    if (!Panel.ClientRectangle.Contains(Panel.PointToClient(Cursor.Position)))
                    {
                        SetHover(false);
                    }
                };
            }
        }

        public Panel Panel { get; }

        public FavoriteFile? File { get; private set; }

        public int Top
        {
            set => Panel.Top = value;
        }

        public void Update(FavoriteFile? file, ToolTip toolTip)
        {
            File = file;
            Panel.Visible = file is not null;
            if (file is null)
            {
                _icon.Image = null;
                _iconPath = null;
                return;
            }

            var exists = System.IO.File.Exists(file.Path);
            if (!string.Equals(_iconPath, file.Path, StringComparison.OrdinalIgnoreCase))
            {
                _icon.Image = ShellIconProvider.FileIcon(file.Path);
                _iconPath = file.Path;
            }

            _name.Text = file.FileName;
            _name.ForeColor = exists ? Theme.Text : Theme.Dim;
            var tip = $"{file.Path}\r\n收藏于 {TimeText.Clock(file.AddedAt)}"
                + (exists ? string.Empty : "\r\n文件已不存在或已被移动");
            toolTip.SetToolTip(Panel, tip);
            toolTip.SetToolTip(_icon, tip);
            toolTip.SetToolTip(_name, tip);
        }

        private void SetHover(bool hover)
        {
            var color = hover ? Theme.Card : Theme.Panel;
            Panel.BackColor = color;
            _icon.BackColor = color;
            _name.BackColor = color;
        }

        private void HandleMouseDown(object? sender, MouseEventArgs e)
        {
            if (File is null)
            {
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                _owner._menuTarget = File;
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                _mouseDownPosition = Cursor.Position;
                _dragCandidate = true;
            }
        }

        private void HandleMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_dragCandidate || File is null)
            {
                return;
            }

            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                _dragCandidate = false;
                return;
            }

            var cursor = Cursor.Position;
            if (Math.Abs(cursor.X - _mouseDownPosition.X) < SystemInformation.DragSize.Width
                && Math.Abs(cursor.Y - _mouseDownPosition.Y) < SystemInformation.DragSize.Height)
            {
                return;
            }

            _dragCandidate = false;
            ShellFileDrop.DoInternalFileDrag(Panel, File.Path);
        }

        private void HandleMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            var shouldOpen = _dragCandidate;
            _dragCandidate = false;
            if (shouldOpen && File is not null)
            {
                _owner._command(File, FileCommand.Open);
            }
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
