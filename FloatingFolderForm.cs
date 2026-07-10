using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DiskWriteWatcher;

public sealed class FloatingFolderForm : Form
{
    private const int CornerRadius = 12;
    private const double FloatingOpacity = 0.70;
    private static readonly Color SurfaceColor = Color.FromArgb(8, 12, 18);
    private static readonly Color TransparentColor = Color.Fuchsia;
    private readonly Action _openLatestFolder;
    private readonly Action<string> _openFavoriteFolder;
    private readonly Action _restoreMainWindow;
    private readonly Action _toggleMonitoring;
    private readonly Action _exitApplication;
    private readonly Action<string> _copyPath;
    private readonly Action<string> _addFavorite;
    private readonly Action<Point> _savePosition;
    private readonly Func<bool> _isMonitoring;
    private readonly Panel _surface = new();
    private readonly Panel _activeSegment = new();
    private readonly Panel _favoriteSegment = new();
    private readonly PictureBox _activeIcon = new();
    private readonly PictureBox _favoriteIcon = new();
    private readonly Label _activeName = new();
    private readonly Label _favoriteName = new();
    private readonly Label _chevron = new();
    private readonly StatusDot _activeDot = new(Theme.Accent);
    private readonly StatusDot _favoriteDot = new(Color.FromArgb(228, 182, 80));
    private readonly ToolTip _toolTip = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _openTargetItem = new();
    private readonly ToolStripMenuItem _copyPathItem = new();
    private readonly ToolStripMenuItem _addFavoriteItem = new();
    private readonly ToolStripMenuItem _toggleMonitoringItem = new();
    private readonly Font _nameFont = new("Microsoft YaHei UI", 8F, FontStyle.Bold);
    private readonly Font _chevronFont = new("Segoe MDL2 Assets", 8F);
    private readonly System.Windows.Forms.Timer _highlightTimer = new() { Interval = 700 };
    private readonly System.Windows.Forms.Timer _favoriteClickTimer = new();
    private readonly System.Windows.Forms.Timer _collapseTimer = new() { Interval = 200 };
    private readonly FavoriteQuickMenuForm _quickMenu;
    private readonly FavoriteInfoPopupForm _infoPopup = new();
    private IReadOnlyList<FavoriteFolder> _favorites = Array.Empty<FavoriteFolder>();
    private Point _dragStart;
    private Point _windowStart;
    private Control? _dragCaptureControl;
    private bool _dragCandidate;
    private bool _dragged;
    private bool _suppressClick;
    private bool _highlightBorder;
    private string? _latestPath;
    private string? _contextTargetPath;
    private bool _contextTargetIsFavorite;

    public FloatingFolderForm(
        Action openLatestFolder,
        Action<string> openFavoriteFolder,
        Action restoreMainWindow,
        Action toggleMonitoring,
        Action exitApplication,
        Action<string> copyPath,
        Action<string> addFavorite,
        Action<Point> savePosition,
        Func<bool> isMonitoring)
    {
        _openLatestFolder = openLatestFolder;
        _openFavoriteFolder = openFavoriteFolder;
        _restoreMainWindow = restoreMainWindow;
        _toggleMonitoring = toggleMonitoring;
        _exitApplication = exitApplication;
        _copyPath = copyPath;
        _addFavorite = addFavorite;
        _savePosition = savePosition;
        _isMonitoring = isMonitoring;

        FormBorderStyle = FormBorderStyle.None;
        Text = "活跃文件夹快捷入口";
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(208, 86);
        BackColor = TransparentColor;
        TransparencyKey = TransparentColor;
        Opacity = FloatingOpacity;
        DoubleBuffered = true;

        BuildUi();
        BuildMenu();
        _quickMenu = new FavoriteQuickMenuForm(
            OpenFavoriteFromMenu,
            ShowFavoriteInfo,
            HideFavoriteInfo,
            ScheduleAutoCollapse,
            PrepareFavoriteContext,
            _menu);

        _favoriteClickTimer.Interval = Math.Max(200, SystemInformation.DoubleClickTime);
        _favoriteClickTimer.Tick += (_, _) =>
        {
            _favoriteClickTimer.Stop();
            ToggleFavoriteMenu();
        };
        _highlightTimer.Tick += (_, _) =>
        {
            _highlightTimer.Stop();
            _highlightBorder = false;
            _surface.Invalidate();
        };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!_quickMenu.Visible)
            {
                return;
            }

            if (_menu.Visible || IsPointerWithinFavoriteInteractionArea())
            {
                _collapseTimer.Start();
            }
            else
            {
                CollapseFavoriteMenu();
            }
        };

        LocationChanged += (_, _) => PositionFavoriteMenu();
        Deactivate += (_, _) => ScheduleAutoCollapse();
        SizeChanged += (_, _) => UpdateRoundedRegion();
        UpdateRoundedRegion();
        UpdateLatest(null, monitoring: false);
        UpdateFavorites(Array.Empty<FavoriteFolder>());
    }

    public void UpdateLatest(FolderActivity? folder, bool monitoring)
    {
        var previousPath = _latestPath;
        _latestPath = folder?.FolderPath;
        _activeName.Text = folder?.DisplayName ?? "暂无记录";
        _activeDot.Active = monitoring && folder is not null;

        var tip = folder is null
            ? "暂无活跃文件夹，右键可恢复主窗口"
            : $"{folder.DisplayName}\r\n{folder.FolderPath}";
        SetTip(_activeSegment, tip);
        _toolTip.SetToolTip(_activeDot, monitoring ? "监视中" : "已暂停");

        if (Visible
            && !string.IsNullOrWhiteSpace(_latestPath)
            && !string.Equals(previousPath, _latestPath, StringComparison.OrdinalIgnoreCase))
        {
            _highlightBorder = true;
            _highlightTimer.Stop();
            _highlightTimer.Start();
            _surface.Invalidate();
        }
    }

    public void UpdateFavorites(IReadOnlyList<FavoriteFolder> favorites)
    {
        _favorites = favorites.Take(5).ToArray();
        var first = _favorites.FirstOrDefault();
        _favoriteName.Text = first?.DisplayName ?? "暂无收藏";
        _favoriteDot.Active = first is not null;
        _chevron.Visible = _favorites.Count > 1;

        var tip = first is null
            ? "暂无收藏文件夹"
            : $"{first.DisplayName}\r\n{first.Path}";
        SetTip(_favoriteSegment, tip);
        _quickMenu.UpdateFavorites(_favorites.Skip(1).Take(4).ToArray());

        if (_quickMenu.Visible)
        {
            if (_favorites.Count <= 1)
            {
                CollapseFavoriteMenu();
            }
            else
            {
                PositionFavoriteMenu();
            }
        }
    }

    public void ShowAt(Point? savedLocation)
    {
        Location = FloatingWindowPlacement.Resolve(savedLocation, Size);
        Show();
        BringToFront();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            CollapseFavoriteMenu();
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

    private void PaintSurface(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (_highlightBorder)
        {
            var borderBounds = Rectangle.Inflate(_surface.ClientRectangle, -1, -1);
            using var path = CreateRoundedPath(borderBounds, CornerRadius - 1);
            using var border = new Pen(Theme.Accent);
            e.Graphics.DrawPath(border, path);
        }

        using var divider = new Pen(Theme.Border);
        e.Graphics.DrawLine(divider, _surface.Width / 2, 8, _surface.Width / 2, _surface.Height - 8);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _highlightTimer.Dispose();
            _favoriteClickTimer.Dispose();
            _collapseTimer.Dispose();
            _quickMenu.Dispose();
            _infoPopup.Dispose();
            _toolTip.Dispose();
            _menu.Dispose();
            _nameFont.Dispose();
            _chevronFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        _surface.Bounds = ClientRectangle;
        _surface.BackColor = SurfaceColor;
        _surface.Paint += PaintSurface;
        Controls.Add(_surface);

        ConfigureSegment(_activeSegment, new Rectangle(1, 1, 103, 84));
        ConfigureSegment(_favoriteSegment, new Rectangle(104, 1, 103, 84));
        _surface.Controls.Add(_activeSegment);
        _surface.Controls.Add(_favoriteSegment);

        ConfigureFolderIcon(_activeIcon, new Point(28, 5));
        ConfigureFolderIcon(_favoriteIcon, new Point(28, 5));
        _activeSegment.Controls.Add(_activeIcon);
        _favoriteSegment.Controls.Add(_favoriteIcon);

        ConfigureName(_activeName);
        ConfigureName(_favoriteName);
        _activeSegment.Controls.Add(_activeName);
        _favoriteSegment.Controls.Add(_favoriteName);

        _activeDot.Location = new Point(86, 7);
        _favoriteDot.Location = new Point(86, 7);
        _activeSegment.Controls.Add(_activeDot);
        _favoriteSegment.Controls.Add(_favoriteDot);

        _chevron.Font = _chevronFont;
        _chevron.ForeColor = Theme.Muted;
        _chevron.BackColor = Color.Transparent;
        _chevron.Text = "\uE70D";
        _chevron.TextAlign = ContentAlignment.MiddleCenter;
        _chevron.Location = new Point(82, 36);
        _chevron.Size = new Size(14, 14);
        _favoriteSegment.Controls.Add(_chevron);

        WireDrag(this);
        WireDrag(_surface);
        MouseDown += HandleActiveContextMouseDown;
        _surface.MouseDown += HandleActiveContextMouseDown;
        foreach (var control in SegmentControls(_activeSegment))
        {
            WireDrag(control);
            control.MouseDoubleClick += HandleActiveDoubleClick;
            control.MouseDown += HandleActiveContextMouseDown;
        }

        foreach (var control in SegmentControls(_favoriteSegment))
        {
            WireDrag(control);
            control.MouseClick += HandleFavoriteMouseClick;
            control.MouseDoubleClick += HandleFavoriteDoubleClick;
            control.MouseEnter += HandleFavoriteMouseEnter;
            control.MouseLeave += HandleFavoriteMouseLeave;
            control.MouseDown += HandleFavoriteContextMouseDown;
        }
    }

    private void BuildMenu()
    {
        _menu.BackColor = Theme.Panel;
        _menu.ForeColor = Theme.Text;
        _menu.ShowImageMargin = false;
        _openTargetItem.Click += (_, _) => OpenContextTarget();
        _copyPathItem.Click += (_, _) => CopyContextTarget();
        _addFavoriteItem.Click += (_, _) => AddContextTargetToFavorites();
        _menu.Items.Add(_openTargetItem);
        _menu.Items.Add(_copyPathItem);
        _menu.Items.Add(_addFavoriteItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("恢复主窗口", null, (_, _) => _restoreMainWindow());
        _toggleMonitoringItem.Click += (_, _) => _toggleMonitoring();
        _menu.Items.Add(_toggleMonitoringItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => _exitApplication());
        _menu.Opening += (_, _) =>
        {
            var hasTarget = !string.IsNullOrWhiteSpace(_contextTargetPath);
            _openTargetItem.Text = _contextTargetIsFavorite ? "打开收藏文件夹" : "打开最新文件夹";
            _openTargetItem.Enabled = hasTarget;
            _copyPathItem.Text = "复制文件夹路径";
            _copyPathItem.Enabled = hasTarget;
            _addFavoriteItem.Text = _contextTargetIsFavorite ? "已收藏" : "加入收藏";
            _addFavoriteItem.Enabled = hasTarget && !_contextTargetIsFavorite;
            _toggleMonitoringItem.Text = _isMonitoring() ? "暂停监视" : "继续监视";
        };

        ContextMenuStrip = _menu;
        _surface.ContextMenuStrip = _menu;
        foreach (Control control in SegmentControls(_activeSegment).Concat(SegmentControls(_favoriteSegment)))
        {
            control.ContextMenuStrip = _menu;
        }
    }

    private void ConfigureSegment(Panel panel, Rectangle bounds)
    {
        panel.Bounds = bounds;
        panel.BackColor = SurfaceColor;
        panel.Cursor = Cursors.Hand;
        panel.MouseEnter += (_, _) => panel.BackColor = Theme.Panel;
        panel.MouseLeave += (_, _) => panel.BackColor = SurfaceColor;
    }

    private void ConfigureFolderIcon(PictureBox icon, Point location)
    {
        icon.Image = ShellIconProvider.FolderIcon();
        icon.SizeMode = PictureBoxSizeMode.CenterImage;
        icon.BackColor = Color.Transparent;
        icon.Location = location;
        icon.Size = new Size(48, 48);
        icon.Cursor = Cursors.Hand;
    }

    private void ConfigureName(Label label)
    {
        label.AutoEllipsis = true;
        label.BackColor = Color.Transparent;
        label.ForeColor = Theme.Text;
        label.Font = _nameFont;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.Location = new Point(9, 54);
        label.Size = new Size(85, 19);
        label.Cursor = Cursors.Hand;
    }

    private static IEnumerable<Control> SegmentControls(Control segment)
    {
        yield return segment;
        foreach (Control child in segment.Controls)
        {
            yield return child;
        }
    }

    private void WireDrag(Control control)
    {
        control.MouseDown += HandleMouseDown;
        control.MouseMove += HandleMouseMove;
        control.MouseUp += HandleMouseUp;
    }

    private void HandleMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _favoriteClickTimer.Stop();
        ReleaseDragCapture();
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

    private void HandleMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragCandidate)
        {
            return;
        }

        if ((Control.MouseButtons & MouseButtons.Left) == 0)
        {
            _dragCandidate = false;
            _dragged = false;
            ReleaseDragCapture();
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
            _infoPopup.Hide();
        }
    }

    private void HandleMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_dragCandidate)
        {
            return;
        }

        _dragCandidate = false;
        ReleaseDragCapture();
        if (_dragged)
        {
            Location = FloatingWindowPlacement.Clamp(Location, Size);
            _savePosition(Location);
            _suppressClick = true;
            BeginInvoke(() => _suppressClick = false);
        }
    }

    private void ReleaseDragCapture()
    {
        if (_dragCaptureControl is not null)
        {
            _dragCaptureControl.Capture = false;
            _dragCaptureControl = null;
        }
    }

    private void HandleActiveDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !_dragged && !string.IsNullOrWhiteSpace(_latestPath))
        {
            _openLatestFolder();
        }
    }

    private void HandleFavoriteMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _suppressClick || _dragged || _favorites.Count == 0)
        {
            return;
        }

        _favoriteClickTimer.Stop();
        _favoriteClickTimer.Start();
    }

    private void HandleFavoriteDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _dragged || _favorites.Count == 0)
        {
            return;
        }

        _favoriteClickTimer.Stop();
        CollapseFavoriteMenu();
        _openFavoriteFolder(_favorites[0].Path);
    }

    private void HandleFavoriteMouseEnter(object? sender, EventArgs e)
    {
        if (_favorites.Count > 0)
        {
            ShowFavoriteInfo(_favorites[0], _favoriteSegment.RectangleToScreen(_favoriteSegment.ClientRectangle));
        }
    }

    private void HandleFavoriteMouseLeave(object? sender, EventArgs e)
    {
        BeginInvoke(() =>
        {
            if (!_favoriteSegment.ClientRectangle.Contains(_favoriteSegment.PointToClient(Cursor.Position)))
            {
                HideFavoriteInfo();
            }
        });
    }

    private void HandleActiveContextMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            PrepareContext(_latestPath, isFavorite: IsFavoritePath(_latestPath));
        }
    }

    private void HandleFavoriteContextMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            PrepareFavoriteContext(_favorites.FirstOrDefault());
        }
    }

    private void PrepareFavoriteContext(FavoriteFolder? favorite)
    {
        PrepareContext(favorite?.Path, isFavorite: favorite is not null);
    }

    private void PrepareContext(string? path, bool isFavorite)
    {
        _contextTargetPath = path;
        _contextTargetIsFavorite = isFavorite;
    }

    private bool IsFavoritePath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && _favorites.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private void OpenContextTarget()
    {
        if (!string.IsNullOrWhiteSpace(_contextTargetPath))
        {
            _openFavoriteFolder(_contextTargetPath);
        }
    }

    private void CopyContextTarget()
    {
        if (!string.IsNullOrWhiteSpace(_contextTargetPath))
        {
            _copyPath(_contextTargetPath);
        }
    }

    private void AddContextTargetToFavorites()
    {
        if (!string.IsNullOrWhiteSpace(_contextTargetPath) && !_contextTargetIsFavorite)
        {
            _addFavorite(_contextTargetPath);
        }
    }

    private void ToggleFavoriteMenu()
    {
        if (_quickMenu.Visible)
        {
            CollapseFavoriteMenu();
            return;
        }

        if (_favorites.Count <= 1)
        {
            return;
        }

        _quickMenu.PrepareForShow();
        _quickMenu.Opacity = 0;
        PositionFavoriteMenu();
        _quickMenu.Show();
        PositionFavoriteMenu();
        _quickMenu.BringToFront();
        _chevron.Text = "\uE70E";
        ScheduleAutoCollapse();
        BeginInvoke(() =>
        {
            if (_quickMenu.Visible)
            {
                PositionFavoriteMenu();
                _quickMenu.Opacity = FloatingOpacity;
            }
        });
    }

    private void PositionFavoriteMenu()
    {
        if (IsHandleCreated && _quickMenu.IsHandleCreated
            && GetWindowRect(_quickMenu.Handle, out var menuBounds)
            && _quickMenu.TryGetVisibleContentBounds(out var contentBounds))
        {
            var anchorBounds = _favoriteSegment.RectangleToScreen(_favoriteSegment.ClientRectangle);
            var gap = Math.Max(4, (int)Math.Round(6 * DeviceDpi / 96F));
            var nativeArea = Screen.FromRectangle(anchorBounds).WorkingArea;
            var contentX = anchorBounds.Right - contentBounds.Width;
            var contentY = anchorBounds.Top - contentBounds.Height - gap;
            if (contentY < nativeArea.Top)
            {
                contentY = Math.Min(nativeArea.Bottom - contentBounds.Height, anchorBounds.Bottom + gap);
            }

            contentX = Math.Clamp(
                contentX,
                nativeArea.Left,
                Math.Max(nativeArea.Left, nativeArea.Right - contentBounds.Width));
            contentY = Math.Clamp(
                contentY,
                nativeArea.Top,
                Math.Max(nativeArea.Top, nativeArea.Bottom - contentBounds.Height));
            var formX = menuBounds.Left + contentX - contentBounds.Left;
            var formY = menuBounds.Top + contentY - contentBounds.Top;
            _ = SetWindowPos(
                _quickMenu.Handle,
                IntPtr.Zero,
                formX,
                formY,
                0,
                0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
            return;
        }

        var desired = new Point(Right - _quickMenu.Width, Top - _quickMenu.Height - 6);
        var area = Screen.FromControl(this).WorkingArea;
        if (desired.Y < area.Top)
        {
            desired.Y = Math.Min(area.Bottom - _quickMenu.Height, Bottom + 6);
        }

        desired.X = Math.Clamp(desired.X, area.Left, Math.Max(area.Left, area.Right - _quickMenu.Width));
        desired.Y = Math.Clamp(desired.Y, area.Top, Math.Max(area.Top, area.Bottom - _quickMenu.Height));
        _quickMenu.Location = desired;
    }

    private void CollapseFavoriteMenu()
    {
        _favoriteClickTimer.Stop();
        _quickMenu.Hide();
        _infoPopup.Hide();
        _chevron.Text = "\uE70D";
    }

    private void OpenFavoriteFromMenu(FavoriteFolder favorite)
    {
        CollapseFavoriteMenu();
        _openFavoriteFolder(favorite.Path);
    }

    private void ShowFavoriteInfo(FavoriteFolder favorite, Rectangle anchor)
    {
        _infoPopup.ShowFor(favorite, anchor);
    }

    private void HideFavoriteInfo()
    {
        _infoPopup.Hide();
    }

    private void ScheduleAutoCollapse()
    {
        if (_quickMenu.Visible)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    private bool IsPointerWithinFavoriteInteractionArea()
    {
        if (!GetCursorPos(out var cursor)
            || !GetWindowRect(Handle, out var anchorBounds)
            || !GetWindowRect(_quickMenu.Handle, out var menuBounds))
        {
            return false;
        }

        var interactionArea = Rectangle.Union(anchorBounds.ToRectangle(), menuBounds.ToRectangle());
        interactionArea.Inflate(12, 12);
        if (interactionArea.Contains(cursor.X, cursor.Y))
        {
            return true;
        }

        return _infoPopup.Visible
            && GetWindowRect(_infoPopup.Handle, out var infoBounds)
            && infoBounds.ToRectangle().Contains(cursor.X, cursor.Y);
    }

    private void SetTip(Control parent, string tip)
    {
        _toolTip.SetToolTip(parent, tip);
        foreach (Control child in parent.Controls)
        {
            _toolTip.SetToolTip(child, tip);
        }
    }

    private void UpdateRoundedRegion()
    {
        _surface.Bounds = ClientRectangle;
        _surface.Region?.Dispose();
        _surface.Region = CreateNativeRoundedRegion(_surface.ClientSize, CornerRadius);
    }

    internal static Region CreateNativeRoundedRegion(Size size, int radius)
    {
        var regionHandle = CreateRoundRectRgn(0, 0, size.Width + 1, size.Height + 1, radius * 2, radius * 2);
        try
        {
            return Region.FromHrgn(regionHandle);
        }
        finally
        {
            _ = DeleteObject(regionHandle);
        }
    }

    internal static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            path.AddRectangle(bounds);
            return path;
        }

        bounds.Width -= 1;
        bounds.Height -= 1;
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal sealed class StatusDot : Control
    {
        private readonly Color _activeColor;
        private bool _active;

        public StatusDot(Color activeColor)
        {
            _activeColor = activeColor;
            Size = new Size(9, 9);
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        public bool Active
        {
            get => _active;
            set
            {
                _active = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Active ? _activeColor : Theme.Dim);
            e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;

        public Rectangle ToRectangle()
        {
            return Rectangle.FromLTRB(Left, Top, Right, Bottom);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;
    }
}

public static class FloatingWindowPlacement
{
    private const int ScreenMargin = 24;

    public static Point Resolve(Point? savedLocation, Size windowSize)
    {
        if (savedLocation is Point saved)
        {
            var savedBounds = new Rectangle(saved, windowSize);
            if (Screen.AllScreens.Any(screen => screen.WorkingArea.Contains(savedBounds)))
            {
                return saved;
            }
        }

        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(Point.Empty);
        return new Point(
            workingArea.Right - windowSize.Width - ScreenMargin,
            workingArea.Bottom - windowSize.Height - ScreenMargin);
    }

    public static Point Clamp(Point location, Size windowSize)
    {
        var center = new Point(location.X + windowSize.Width / 2, location.Y + windowSize.Height / 2);
        var screen = Screen.FromPoint(center);
        var area = screen.WorkingArea;
        return new Point(
            Math.Clamp(location.X, area.Left, Math.Max(area.Left, area.Right - windowSize.Width)),
            Math.Clamp(location.Y, area.Top, Math.Max(area.Top, area.Bottom - windowSize.Height)));
    }
}
