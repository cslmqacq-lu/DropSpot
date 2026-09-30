using System.Runtime.InteropServices;

namespace DropSpot;

internal sealed class PinnedFolderForm : Form
{
    private readonly Action<string> _openFolder;
    private readonly Action<string> _copyPath;
    private readonly Action<string> _addFavorite;
    private readonly Action<string> _unpin;
    private readonly Action<string, Point> _savePosition;
    private readonly LayeredImageBackdropForm _backdrop = new();
    private readonly ToolTip _toolTip = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly Panel _surface = new();
    private readonly PictureBox _icon = new();
    private readonly Label _name = new();
    private readonly Panel _activityDot = new();
    private PinnedFolder _folder;
    private Point _dragStart;
    private Point _windowStart;
    private Control? _dragCaptureControl;
    private bool _dragCandidate;
    private bool _dragged;

    public PinnedFolderForm(
        PinnedFolder folder,
        Action<string> openFolder,
        Action<string> copyPath,
        Action<string> addFavorite,
        Action<string> unpin,
        Action<string, Point> savePosition)
    {
        _folder = folder;
        _openFolder = openFolder;
        _copyPath = copyPath;
        _addFavorite = addFavorite;
        _unpin = unpin;
        _savePosition = savePosition;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Size = FloatingFrameAssets.PinnedFrame.Size;
        TransparentWindowStyle.ApplyToForeground(this);
        DoubleBuffered = true;

        BuildUi();
        BuildMenu();
        _backdrop.SetImage(FloatingFrameAssets.PinnedFrame);
        LocationChanged += (_, _) => SyncBackdrop();
        SizeChanged += (_, _) => SyncBackdrop();
        UpdatePinnedFolder(folder);
    }

    public string FolderPath => _folder.Path;

    public void UpdatePinnedFolder(PinnedFolder folder)
    {
        _folder = folder;
        _name.Text = folder.DisplayName;
        _activityDot.Visible = folder.HasUnreadActivity;
        var activity = folder.LastActivity == default ? "" : $"\n最后更新：{folder.LastActivity:MM-dd HH:mm:ss}";
        var unread = folder.HasUnreadActivity ? "\n有新的文件活动" : string.Empty;
        var tooltip = folder.Path + activity + unread;
        _toolTip.SetToolTip(_surface, tooltip);
        _toolTip.SetToolTip(_icon, tooltip);
        _toolTip.SetToolTip(_name, tooltip);
        _toolTip.SetToolTip(_activityDot, tooltip);
    }

    public void ShowAt(Point? savedLocation)
    {
        Location = FloatingWindowPlacement.Resolve(savedLocation, Size);
        Show();
        SyncBackdrop();
        _backdrop.ShowBehind(this);
        BringToFront();
    }

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

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            BeginInvoke(SyncBackdrop);
        }
        else
        {
            _backdrop.Hide();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var noSystemCorner = 1;
        _ = DwmSetWindowAttribute(Handle, 33, ref noSystemCorner, sizeof(int));
        var noSystemBorder = unchecked((int)0xFFFFFFFE);
        _ = DwmSetWindowAttribute(Handle, 34, ref noSystemBorder, sizeof(int));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _backdrop.Dispose();
            _toolTip.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        _surface.Dock = DockStyle.Fill;
        _surface.BackColor = Color.Transparent;
        _surface.Cursor = Cursors.Hand;
        Controls.Add(_surface);

        _icon.Image = ShellIconProvider.FolderIcon();
        _icon.BackColor = Color.Transparent;
        _icon.SizeMode = PictureBoxSizeMode.CenterImage;
        _icon.Location = new Point(30, 7);
        _icon.Size = new Size(48, 48);
        _icon.Cursor = Cursors.Hand;
        _surface.Controls.Add(_icon);

        _name.AutoEllipsis = true;
        _name.TextAlign = ContentAlignment.MiddleCenter;
        _name.BackColor = Color.Transparent;
        _name.ForeColor = Theme.Text;
        _name.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
        _name.Location = new Point(11, 61);
        _name.Size = new Size(86, 18);
        _name.Cursor = Cursors.Hand;
        _surface.Controls.Add(_name);

        _activityDot.BackColor = Theme.Accent;
        _activityDot.Location = new Point(83, 10);
        _activityDot.Size = new Size(8, 8);
        _activityDot.Cursor = Cursors.Hand;
        _activityDot.Visible = false;
        _surface.Controls.Add(_activityDot);
        _activityDot.BringToFront();

        foreach (var control in new Control[] { _surface, _icon, _name, _activityDot })
        {
            control.DoubleClick += (_, _) => _openFolder(_folder.Path);
            control.MouseDown += BeginDrag;
            control.MouseMove += ContinueDrag;
            control.MouseUp += EndDrag;
        }
    }

    private void BuildMenu()
    {
        _menu.ShowImageMargin = false;
        _menu.Items.Add("打开文件夹", null, (_, _) => _openFolder(_folder.Path));
        _menu.Items.Add("复制文件夹路径", null, (_, _) => _copyPath(_folder.Path));
        _menu.Items.Add("加入收藏", null, (_, _) => _addFavorite(_folder.Path));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("取消钉住", null, (_, _) => _unpin(_folder.Path));

        ContextMenuStrip = _menu;
        _surface.ContextMenuStrip = _menu;
        _icon.ContextMenuStrip = _menu;
        _name.ContextMenuStrip = _menu;
        _activityDot.ContextMenuStrip = _menu;
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragCaptureControl = sender as Control;
        if (_dragCaptureControl is not null)
        {
            _dragCaptureControl.Capture = true;
        }

        _dragStart = Cursor.Position;
        _windowStart = Location;
        _dragCandidate = true;
        _dragged = false;
    }

    private void ContinueDrag(object? sender, MouseEventArgs e)
    {
        if (!_dragCandidate || e.Button != MouseButtons.Left)
        {
            return;
        }

        var cursor = Cursor.Position;
        var delta = new Size(cursor.X - _dragStart.X, cursor.Y - _dragStart.Y);
        if (!_dragged
            && (Math.Abs(delta.Width) >= SystemInformation.DragSize.Width / 2
                || Math.Abs(delta.Height) >= SystemInformation.DragSize.Height / 2))
        {
            _dragged = true;
        }

        if (_dragged)
        {
            Location = _windowStart + delta;
        }
    }

    private void EndDrag(object? sender, MouseEventArgs e)
    {
        if (!_dragCandidate || e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragCandidate = false;
        if (_dragCaptureControl is not null)
        {
            _dragCaptureControl.Capture = false;
            _dragCaptureControl = null;
        }

        if (_dragged)
        {
            Location = FloatingWindowPlacement.Clamp(Location, Size);
            _savePosition(_folder.Path, Location);
        }

        _dragged = false;
    }

    private void SyncBackdrop()
    {
        if (!IsDisposed && !_backdrop.IsDisposed)
        {
            _backdrop.SyncTo(this);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
