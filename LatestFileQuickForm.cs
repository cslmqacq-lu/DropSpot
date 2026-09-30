using System.Runtime.InteropServices;

namespace DropSpot;

internal sealed class LatestFileQuickForm : Form
{
    private const int AnchorGap = 6;
    private readonly Action<ChangeRecord> _openFile;
    private readonly Action<string> _openFolder;
    private readonly Action<string> _copyPath;
    private readonly LayeredImageBackdropForm _backdrop = new();
    private readonly Panel _surface = new();
    private readonly PictureBox _icon = new();
    private readonly Label _name = new();
    private readonly ToolTip _toolTip = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly Font _nameFont = new("Microsoft YaHei UI", 7.5F, FontStyle.Bold);
    private ChangeRecord? _record;
    private Point _mouseDownPosition;
    private Control? _captureControl;
    private bool _dragCandidate;
    private bool _dragged;

    public LatestFileQuickForm(
        Action<ChangeRecord> openFile,
        Action<string> openFolder,
        Action<string> copyPath)
    {
        _openFile = openFile;
        _openFolder = openFolder;
        _copyPath = copyPath;

        FormBorderStyle = FormBorderStyle.None;
        Text = "最近文件";
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = FloatingFrameAssets.FavoriteFrame.Size;
        TransparentWindowStyle.ApplyToForeground(this);
        DoubleBuffered = true;

        BuildUi();
        BuildMenu();
        _backdrop.SetImage(FloatingFrameAssets.FavoriteFrame);
        LocationChanged += (_, _) => SyncBackdrop();
        SizeChanged += (_, _) => SyncBackdrop();
    }

    internal string? DisplayedFilePath => _record?.FilePath;
    internal bool IsDragging => _dragged;

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

    public void UpdateFile(ChangeRecord? record)
    {
        _record = record;
        if (record is null)
        {
            _name.Text = string.Empty;
            _icon.Image = null;
            HideCard();
            return;
        }

        _icon.Image = ShellIconProvider.LargeFileIcon(record.FilePath);
        _name.Text = record.FileName;
        SetToolTip(record.FilePath);
    }

    public void ShowAbove(Rectangle anchorBounds)
    {
        if (_record is null)
        {
            HideCard();
            return;
        }

        var left = anchorBounds.Left + (anchorBounds.Width - Width) / 2;
        var area = Screen.FromRectangle(anchorBounds).WorkingArea;
        left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - Width));
        Location = new Point(left, anchorBounds.Top - Height - AnchorGap);

        if (!Visible)
        {
            Show();
        }

        SyncBackdrop();
        BringToFront();
        _backdrop.ShowBehind(this);
    }

    public void HideCard()
    {
        ReleaseDragCapture();
        _dragCandidate = false;
        _dragged = false;
        Hide();
        _backdrop.Hide();
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
            _nameFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        _surface.Bounds = ClientRectangle;
        _surface.BackColor = Color.Transparent;
        _surface.Cursor = Cursors.Hand;
        Controls.Add(_surface);

        _icon.Image = ShellIconProvider.LargeFileIcon("file.bin");
        _icon.SizeMode = PictureBoxSizeMode.CenterImage;
        _icon.BackColor = Color.Transparent;
        _icon.Location = new Point(30, 9);
        _icon.Size = new Size(48, 44);
        _surface.Controls.Add(_icon);

        _name.AutoEllipsis = true;
        _name.Font = _nameFont;
        _name.ForeColor = Theme.Text;
        _name.BackColor = Color.Transparent;
        _name.TextAlign = ContentAlignment.MiddleCenter;
        _name.Location = new Point(11, 61);
        _name.Size = new Size(86, 18);
        _surface.Controls.Add(_name);

        foreach (var control in _surface.Controls.Cast<Control>().Prepend(_surface))
        {
            control.Cursor = Cursors.Hand;
            control.MouseDown += HandleMouseDown;
            control.MouseMove += HandleMouseMove;
            control.MouseUp += HandleMouseUp;
        }
    }

    private void BuildMenu()
    {
        _menu.BackColor = Theme.Panel;
        _menu.ForeColor = Theme.Text;
        _menu.ShowImageMargin = false;
        _menu.Items.Add("打开文件", null, (_, _) => OpenCurrent());
        _menu.Items.Add("打开所在文件夹", null, (_, _) => OpenCurrentFolder());
        _menu.Items.Add("复制文件路径", null, (_, _) => CopyCurrentPath());

        _surface.ContextMenuStrip = _menu;
        foreach (Control control in _surface.Controls)
        {
            control.ContextMenuStrip = _menu;
        }
    }

    private void HandleMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _record is null)
        {
            return;
        }

        ReleaseDragCapture();
        _captureControl = sender as Control;
        if (_captureControl is not null)
        {
            _captureControl.Capture = true;
        }

        _mouseDownPosition = Cursor.Position;
        _dragCandidate = true;
        _dragged = false;
    }

    private void HandleMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragCandidate || _record is null)
        {
            return;
        }

        if ((Control.MouseButtons & MouseButtons.Left) == 0)
        {
            _dragCandidate = false;
            ReleaseDragCapture();
            return;
        }

        var cursor = Cursor.Position;
        var deltaX = Math.Abs(cursor.X - _mouseDownPosition.X);
        var deltaY = Math.Abs(cursor.Y - _mouseDownPosition.Y);
        if (deltaX < SystemInformation.DragSize.Width / 2
            && deltaY < SystemInformation.DragSize.Height / 2)
        {
            return;
        }

        var dragPath = _record.FilePath;
        _dragCandidate = false;
        _dragged = true;
        ReleaseDragCapture();
        var data = new DataObject(DataFormats.FileDrop, new[] { dragPath });
        DoDragDrop(data, DragDropEffects.Copy);
    }

    private void HandleMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var shouldOpen = _dragCandidate && !_dragged;
        _dragCandidate = false;
        ReleaseDragCapture();
        if (shouldOpen)
        {
            OpenCurrent();
        }
    }

    private void OpenCurrent()
    {
        if (_record is not null)
        {
            _openFile(_record);
        }
    }

    private void OpenCurrentFolder()
    {
        if (_record is not null)
        {
            _openFolder(_record.FolderPath);
        }
    }

    private void CopyCurrentPath()
    {
        if (_record is not null)
        {
            _copyPath(_record.FilePath);
        }
    }

    private void SetToolTip(string path)
    {
        _toolTip.SetToolTip(_surface, path);
        _toolTip.SetToolTip(_icon, path);
        _toolTip.SetToolTip(_name, path);
    }

    private void ReleaseDragCapture()
    {
        if (_captureControl is not null)
        {
            _captureControl.Capture = false;
            _captureControl = null;
        }
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
