using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace DropSpot;

/// <summary>悬浮舱需要宿主（MainForm）提供的操作。</summary>
internal interface ICapsuleHost
{
    void OpenLatestFolder();
    void OpenActivityFolderPath(string folderPath);
    void RunFileCommandForPath(string filePath, FileCommand command);
    ContextMenuStrip BuildFolderMenu(string folderPath, bool isFavorite);
    ContextMenuStrip BuildFileMenu(string filePath);
    void ToggleMonitoring();
    void OpenSettingsDialog();
    void ShowHistoryDialog();
    void ShowDiagnosticsDialog();
    void ClearRecent();
    void AddFavoriteFolderDialog();
    void AuthorizeMonitoring();
    void ExitApplication();
    void HandleDroppedPaths(string[] paths);
    void SaveCapsuleLocation(Point location);
}

internal sealed record CapsuleFile(string Path, string FolderPath, string FolderName, DateTime Time, bool Exists)
{
    public string Name => System.IO.Path.GetFileName(Path);

    public string Ext
    {
        get
        {
            var ext = System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
            return ext.Length == 0 ? "FILE" : ext.Length > 4 ? ext[..4] : ext;
        }
    }
}

internal sealed record CapsuleFolder(string Path, string Name, bool Fresh);

internal sealed record CapsuleSnapshot(
    string? LatestFolderPath,
    string LatestFolderName,
    DateTime? LatestTime,
    int LatestCount,
    IReadOnlyList<CapsuleFile> RecentFiles,
    IReadOnlyList<CapsuleFolder> ActiveFolders,
    IReadOnlyList<CapsuleFolder> Favorites,
    IReadOnlyList<CapsuleFile> StarredFiles,
    bool Monitoring,
    string StatusText,
    bool NeedsAuthorization,
    string AuthorizationText,
    string AuthorizationButton)
{
    public static CapsuleSnapshot Empty { get; } = new(
        null, "暂无记录", null, 0,
        Array.Empty<CapsuleFile>(), Array.Empty<CapsuleFolder>(), Array.Empty<CapsuleFolder>(), Array.Empty<CapsuleFile>(),
        false, "未开始监视", false, string.Empty, "授权");
}

internal enum CapsuleMode
{
    Rest,
    Edge,
    Peek,
    Drop,
    Toast
}

/// <summary>
/// 悬浮舱：收起时是 104×96 的小卡片，鼠标悬停展开成面板，离开后收起。
/// 面板按使用频率排布：最新文件 → 活跃文件夹 → 收藏夹 → ★ 收藏文件（默认折叠） → 底栏。
/// 整个窗口自绘，所有可点击区域记录在 _hits 中统一做命中测试。
/// </summary>
internal sealed class CapsuleForm : Form
{
    // ---------- 尺寸（逻辑像素，按 DPI 缩放） ----------
    private const int RestW = 104, RestH = 96;
    private const int ToastW = 272, ToastH = 96;
    private const int EdgeW = 14, EdgeH = 64;
    private const int DropW = 320, DropH = 188;
    private const int PeekW = 320;
    private const int EdgeSnapDistance = 28;

    // ---------- 颜色 ----------
    private static readonly Color PanelColor = Color.FromArgb(16, 23, 34);
    private static readonly Color BorderColor = Color.FromArgb(42, 54, 72);
    private static readonly Color HoverColor = Color.FromArgb(28, 40, 56);
    private static readonly Color TileColor = Color.FromArgb(20, 29, 42);
    private static readonly Color TileBorder = Color.FromArgb(36, 50, 70);
    private static readonly Color Muted = Color.FromArgb(138, 155, 176);
    private static readonly Color Faint = Color.FromArgb(111, 129, 153);
    private static readonly Color Green = Color.FromArgb(61, 220, 132);
    private static readonly Color Gold = Color.FromArgb(242, 196, 92);
    private static readonly Color GoldDark = Color.FromArgb(201, 146, 43);

    private readonly ICapsuleHost _host;
    private readonly System.Windows.Forms.Timer _expandTimer = new() { Interval = 120 };
    private readonly System.Windows.Forms.Timer _collapseTimer = new() { Interval = 450 };
    private readonly System.Windows.Forms.Timer _toastTimer = new() { Interval = 2600 };
    private readonly System.Windows.Forms.Timer _dragLeaveTimer = new() { Interval = 300 };
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 15 };
    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 30_000 };
    private readonly System.Windows.Forms.Timer _messageTimer = new() { Interval = 4000 };
    private readonly ToolTip _toolTip = new() { InitialDelay = 500, ReshowDelay = 200 };
    private readonly List<HitItem> _hits = new();
    private readonly Font _fontTitle = new("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
    private readonly Font _fontBody = new("Microsoft YaHei UI", 9.5F);
    private readonly Font _fontBodyBold = new("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
    private readonly Font _fontSmall = new("Microsoft YaHei UI", 8.25F);
    private readonly Font _fontTiny = new("Microsoft YaHei UI", 7.5F);
    private readonly Font _fontBadge = new("Segoe UI", 6.75F, FontStyle.Bold);
    private readonly Font _fontIcon = new("Segoe MDL2 Assets", 10F);
    private readonly Font _fontStar = new("Segoe UI Symbol", 20F);

    private CapsuleSnapshot _data = CapsuleSnapshot.Empty;
    private CapsuleMode _mode = CapsuleMode.Rest;
    private CapsuleMode _collapsedMode = CapsuleMode.Rest;
    private Rectangle _restBounds;
    private bool _anchorRight = true;
    private bool _anchorBottom = true;
    private bool _edgeOnLeft;
    private bool _pinned;
    private bool _pinnedByTray;
    private bool _contentVisible = true;
    private bool _chipsExpanded;
    private bool _favoritesExpanded;
    private bool _starsExpanded;
    private bool _menuOpen;
    private int _holdOpen;
    private HitItem? _hover;
    private HitItem? _pressed;
    private Point _pressScreen;
    private Point _pressWindowLocation;
    private bool _movingWindow;
    private string? _toastFile;
    private string? _toastFolder;
    private string? _lastRecentPath;
    private DateTime _lastToastAt = DateTime.MinValue;
    private string? _message;
    private Rectangle _animFrom;
    private Rectangle _animTo;
    private DateTime _animStart;
    private Action? _animDone;
    private float _s = 1F;
    // 背景不透明度：100 = 普通窗口（系统圆角 + 阴影 + ClearType）；< 100 = 分层窗口，逐像素透明，文字保持不透明
    private int _opacity = 100;
    private bool _layered;
    private bool _layeredPending;

    public CapsuleForm(ICapsuleHost host)
    {
        _host = host;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = PanelColor;
        ForeColor = Theme.Text;
        Text = "DropSpot";
        DoubleBuffered = true;
        AllowDrop = true;
        KeyPreview = false;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        _expandTimer.Tick += (_, _) =>
        {
            _expandTimer.Stop();
            if (_pressed is null
                && Control.MouseButtons == MouseButtons.None
                && IsCursorInside()
                && _mode is CapsuleMode.Rest or CapsuleMode.Edge or CapsuleMode.Toast)
            {
                Expand(CapsuleMode.Peek);
            }
        };
        _collapseTimer.Tick += (_, _) => TryAutoCollapse();
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            if (_mode == CapsuleMode.Toast)
            {
                Collapse();
            }
        };
        _dragLeaveTimer.Tick += (_, _) =>
        {
            _dragLeaveTimer.Stop();
            if (_mode == CapsuleMode.Drop)
            {
                Collapse();
            }
        };
        _animationTimer.Tick += (_, _) => AnimationStep();
        _clockTimer.Tick += (_, _) =>
        {
            if (_mode == CapsuleMode.Peek)
            {
                Invalidate();
            }
        };
        _clockTimer.Start();
        // 从托盘打开的临时固定：点到别处（失去焦点）后恢复自动收起
        Deactivate += (_, _) =>
        {
            if (_pinnedByTray && _holdOpen == 0)
            {
                _pinnedByTray = false;
                _pinned = false;
                ScheduleCollapseCheck();
            }
        };
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            _message = null;
            Invalidate();
        };
    }

    public bool IsExpanded => _mode is CapsuleMode.Peek or CapsuleMode.Drop;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExToolWindow = 0x00000080;
            const int wsExLayered = 0x00080000;
            var parameters = base.CreateParams;
            parameters.ExStyle |= wsExToolWindow;
            if (_layered)
            {
                parameters.ExStyle |= wsExLayered;
            }
            else
            {
                parameters.ExStyle &= ~wsExLayered;
            }

            return parameters;
        }
    }

    // =====================================================================
    // 对外接口
    // =====================================================================

    /// <summary>在保存的位置显示收起状态（贴在屏幕边缘时自动变成细条）。</summary>
    public void ShowAt(Point? savedLocation)
    {
        _s = DeviceDpi / 96F;
        var restSize = new Size(S(RestW), S(RestH));
        var location = FloatingWindowPlacement.Resolve(savedLocation, restSize);
        _restBounds = new Rectangle(location, restSize);
        _mode = CapsuleMode.Rest;
        _collapsedMode = CapsuleMode.Rest;
        var area = Screen.FromRectangle(_restBounds).WorkingArea;
        if (_restBounds.Left <= area.Left + 1 || _restBounds.Right >= area.Right - 1)
        {
            SnapToEdge(_restBounds.Left <= area.Left + 1);
        }

        Bounds = _mode == CapsuleMode.Edge ? EdgeBounds() : _restBounds;
        ApplyShape();
        RebuildHits();
        if (!Visible)
        {
            Show();
        }

        Invalidate();
    }

    /// <summary>设置背景不透明度（百分比）。文字、图标保持不透明。</summary>
    public void SetBackgroundOpacity(int percent)
    {
        var value = AppSettings.NormalizeCapsuleOpacity(percent);
        if (value == _opacity)
        {
            return;
        }

        _opacity = value;
        var layered = value < 100;
        if (layered != _layered)
        {
            _layered = layered;
            if (IsHandleCreated)
            {
                UpdateStyles();
                ApplyShape();
                if (!layered)
                {
                    Invalidate();
                    Update();
                }
            }
        }

        if (_layered)
        {
            RenderLayered();
        }
    }

    public void UpdateSnapshot(CapsuleSnapshot data)
    {
        var previousRecent = _lastRecentPath;
        _data = data;
        _lastRecentPath = data.RecentFiles.FirstOrDefault()?.Path;
        if (!IsHandleCreated || !Visible)
        {
            return;
        }

        if (previousRecent is not null
            && _lastRecentPath is not null
            && !string.Equals(previousRecent, _lastRecentPath, StringComparison.OrdinalIgnoreCase)
            && _mode is CapsuleMode.Rest or CapsuleMode.Edge
            && DateTime.UtcNow - _lastToastAt > TimeSpan.FromSeconds(4))
        {
            var file = data.RecentFiles[0];
            ShowToast(file.Name, file.FolderName);
            return;
        }

        if (_mode == CapsuleMode.Peek && !_animationTimer.Enabled)
        {
            ResizeForContent();
        }

        RebuildHits();
        Invalidate();
    }

    /// <summary>托盘或快捷入口：展开并固定，直到用户点图钉或点空白处收起。</summary>
    public void ExpandPinned()
    {
        if (!Visible)
        {
            return;
        }

        _pinned = true;
        _pinnedByTray = true;
        Expand(CapsuleMode.Peek);
        Activate();
    }

    public void CollapseNow()
    {
        _pinned = false;
        Collapse();
    }

    /// <summary>在面板底栏短暂显示一条提示（例如“已复制路径”）。</summary>
    public void ShowMessage(string message)
    {
        _message = message;
        _messageTimer.Stop();
        _messageTimer.Start();
        if (_mode == CapsuleMode.Peek)
        {
            Invalidate();
        }
    }

    /// <summary>打开对话框等期间保持展开，避免鼠标移出后收起。</summary>
    public IDisposable HoldOpen()
    {
        _holdOpen++;
        return new Releaser(() =>
        {
            _holdOpen = Math.Max(0, _holdOpen - 1);
            ScheduleCollapseCheck();
        });
    }

    // =====================================================================
    // 状态切换与动画
    // =====================================================================

    private void Expand(CapsuleMode mode)
    {
        _expandTimer.Stop();
        _toastTimer.Stop();
        if (_mode != CapsuleMode.Peek && _mode != CapsuleMode.Drop)
        {
            _collapsedMode = _mode == CapsuleMode.Edge ? CapsuleMode.Edge : _collapsedMode;
            ComputeAnchors();
        }

        _mode = mode;
        var size = mode == CapsuleMode.Drop
            ? new Size(S(DropW), S(DropH))
            : new Size(S(PeekW), MeasurePeekHeight());
        AnimateTo(AnchoredBounds(size), null);
        ScheduleCollapseCheck();
    }

    private void Collapse()
    {
        _collapseTimer.Stop();
        _expandTimer.Stop();
        if (_mode is CapsuleMode.Rest or CapsuleMode.Edge)
        {
            return;
        }

        _menuOpen = false;
        _chipsExpanded = false;
        _favoritesExpanded = false;
        var target = _collapsedMode == CapsuleMode.Edge ? EdgeBounds() : _restBounds;
        var targetMode = _collapsedMode;
        _mode = targetMode;
        AnimateTo(target, null);
    }

    private void ShowToast(string fileName, string folderName)
    {
        _toastFile = fileName;
        _toastFolder = folderName;
        _lastToastAt = DateTime.UtcNow;
        ComputeAnchors();
        _mode = CapsuleMode.Toast;
        AnimateTo(AnchoredBounds(new Size(S(ToastW), S(ToastH))), null);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ResizeForContent()
    {
        var target = AnchoredBounds(new Size(S(PeekW), MeasurePeekHeight()));
        if (target != Bounds)
        {
            AnimateTo(target, null);
        }
    }

    private void AnimateTo(Rectangle target, Action? done)
    {
        target = ClampToScreen(target);
        _animFrom = Bounds;
        _animTo = target;
        _animStart = DateTime.UtcNow;
        _animDone = done;
        _contentVisible = false;
        RebuildHits();
        if (_animFrom == _animTo)
        {
            FinishAnimation();
            return;
        }

        _animationTimer.Start();
    }

    private void AnimationStep()
    {
        var t = Math.Min(1.0, (DateTime.UtcNow - _animStart).TotalMilliseconds / 150.0);
        var e = 1 - Math.Pow(1 - t, 3);
        Rectangle Lerp(Rectangle a, Rectangle b) => new(
            (int)Math.Round(a.X + (b.X - a.X) * e),
            (int)Math.Round(a.Y + (b.Y - a.Y) * e),
            (int)Math.Round(a.Width + (b.Width - a.Width) * e),
            (int)Math.Round(a.Height + (b.Height - a.Height) * e));
        Bounds = Lerp(_animFrom, _animTo);
        ApplyShape();
        if (t >= 1.0)
        {
            FinishAnimation();
        }
    }

    private void FinishAnimation()
    {
        _animationTimer.Stop();
        Bounds = _animTo;
        ApplyShape();
        _contentVisible = true;
        RebuildHits();
        Invalidate();
        var done = _animDone;
        _animDone = null;
        done?.Invoke();
    }

    private void ComputeAnchors()
    {
        var reference = _collapsedMode == CapsuleMode.Edge ? EdgeBounds() : _restBounds;
        var area = Screen.FromRectangle(reference).WorkingArea;
        var center = new Point(reference.Left + reference.Width / 2, reference.Top + reference.Height / 2);
        _anchorRight = _collapsedMode == CapsuleMode.Edge ? !_edgeOnLeft : center.X >= area.Left + area.Width / 2;
        _anchorBottom = center.Y >= area.Top + area.Height / 2;
    }

    private Rectangle AnchoredBounds(Size size)
    {
        var reference = _collapsedMode == CapsuleMode.Edge ? EdgeBounds() : _restBounds;
        var x = _anchorRight ? reference.Right - size.Width : reference.Left;
        var y = _anchorBottom ? reference.Bottom - size.Height : reference.Top;
        return ClampToScreen(new Rectangle(new Point(x, y), size));
    }

    private static Rectangle ClampToScreen(Rectangle bounds)
    {
        var area = Screen.FromRectangle(bounds).WorkingArea;
        var x = Math.Clamp(bounds.X, area.Left, Math.Max(area.Left, area.Right - bounds.Width));
        var y = Math.Clamp(bounds.Y, area.Top, Math.Max(area.Top, area.Bottom - bounds.Height));
        return new Rectangle(x, y, bounds.Width, bounds.Height);
    }

    private void SnapToEdge(bool left)
    {
        _edgeOnLeft = left;
        _mode = CapsuleMode.Edge;
        _collapsedMode = CapsuleMode.Edge;
    }

    private Rectangle EdgeBounds()
    {
        var area = Screen.FromRectangle(_restBounds).WorkingArea;
        var height = S(EdgeH);
        var y = Math.Clamp(_restBounds.Top + (_restBounds.Height - height) / 2, area.Top, area.Bottom - height);
        var x = _edgeOnLeft ? area.Left : area.Right - S(EdgeW);
        return new Rectangle(x, y, S(EdgeW), height);
    }

    private void ApplyShape()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        if (_layered)
        {
            // 分层窗口自己画抗锯齿圆角，角落像素全透明（鼠标可穿透）
            Region = null;
            RenderLayered();
            return;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // Windows 11：系统圆角 + 阴影，边缘抗锯齿
            WindowChrome.RoundCorners(this);
            Region = null;
            return;
        }

        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius());
        Region = new Region(path);
    }

    private int CornerRadius() => _mode switch
    {
        CapsuleMode.Edge => S(7),
        CapsuleMode.Peek or CapsuleMode.Drop => S(16),
        _ => S(22)
    };

    // =====================================================================
    // 半透明（分层窗口）
    // =====================================================================

    protected override void OnInvalidated(InvalidateEventArgs e)
    {
        base.OnInvalidated(e);
        if (_layered && !_layeredPending && IsHandleCreated)
        {
            _layeredPending = true;
            BeginInvoke(new Action(RenderLayered));
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_layered)
        {
            RenderLayered();
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_layered && Visible)
        {
            RenderLayered();
        }
    }

    private void RenderLayered()
    {
        _layeredPending = false;
        if (!_layered || !IsHandleCreated || IsDisposed || Width <= 0 || Height <= 0)
        {
            return;
        }

        try
        {
            using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                PaintSurface(g);
            }

            LayeredNative.Push(this, bitmap);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException)
        {
            AppLog.Warning($"悬浮舱半透明绘制失败：{ex.Message}");
        }
    }

    /// <summary>分层模式下的背景色：深色底面按不透明度降低 alpha，亮色（按钮、图标、文字）保持原样。</summary>
    private Color Surface(Color color)
    {
        if (!_layered)
        {
            return color;
        }

        var luma = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
        return luma < 80 ? Color.FromArgb(color.A * _opacity / 100, color) : color;
    }

    // =====================================================================
    // 鼠标、拖放
    // =====================================================================

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _collapseTimer.Stop();
        if (_mode is CapsuleMode.Rest or CapsuleMode.Edge or CapsuleMode.Toast && !_expandTimer.Enabled && _pressed is null && !_movingWindow)
        {
            _expandTimer.Start();
        }

        if (_pressed is not null && (Control.MouseButtons & MouseButtons.Left) != 0)
        {
            var cursor = Cursor.Position;
            var dx = cursor.X - _pressScreen.X;
            var dy = cursor.Y - _pressScreen.Y;
            var moved = Math.Abs(dx) >= SystemInformation.DragSize.Width || Math.Abs(dy) >= SystemInformation.DragSize.Height;
            if (_movingWindow)
            {
                Location = new Point(_pressWindowLocation.X + dx, _pressWindowLocation.Y + dy);
                return;
            }

            if (moved)
            {
                var pressed = _pressed;
                if (pressed.Kind is HitKind.RecentFile or HitKind.StarFile && pressed.Path is not null)
                {
                    _pressed = null;
                    ShellFileDrop.DoInternalFileDrag(this, pressed.Path);
                    return;
                }

                var movable = (pressed.Kind == HitKind.Body && _mode is CapsuleMode.Rest or CapsuleMode.Edge or CapsuleMode.Toast)
                    || (pressed.Kind == HitKind.Header && _mode == CapsuleMode.Peek);
                if (movable)
                {
                    _expandTimer.Stop();
                    _movingWindow = true;
                    Location = new Point(_pressWindowLocation.X + dx, _pressWindowLocation.Y + dy);
                    return;
                }
            }
        }

        var hit = HitTest(e.Location);
        if (!Equals(hit, _hover))
        {
            _hover = hit;
            Cursor = hit is null ? Cursors.Default : hit.Kind == HitKind.Header ? Cursors.SizeAll : Cursors.Hand;
            _toolTip.SetToolTip(this, hit?.Tip);
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _expandTimer.Stop();
        if (_hover is not null)
        {
            _hover = null;
            Invalidate();
        }

        ScheduleCollapseCheck();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _expandTimer.Stop();
        _pressed = HitTest(e.Location);
        _pressScreen = Cursor.Position;
        _pressWindowLocation = Location;
        _movingWindow = false;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right)
        {
            ShowContextMenu(HitTest(e.Location), e.Location);
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        if (_movingWindow)
        {
            _movingWindow = false;
            FinishWindowMove();
            return;
        }

        var hit = HitTest(e.Location);
        if (pressed is not null && hit is not null && pressed.Kind == hit.Kind && pressed.Index == hit.Index)
        {
            RunHit(hit);
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && _mode is CapsuleMode.Rest or CapsuleMode.Toast)
        {
            _expandTimer.Stop();
            _host.OpenLatestFolder();
        }
    }

    protected override void OnDragEnter(DragEventArgs drgevent)
    {
        base.OnDragEnter(drgevent);
        var accept = !ShellFileDrop.InternalDragActive && drgevent.Data?.GetDataPresent(DataFormats.FileDrop) == true;
        drgevent.Effect = accept ? DragDropEffects.Copy : DragDropEffects.None;
        _dragLeaveTimer.Stop();
        if (accept && _mode != CapsuleMode.Drop)
        {
            Expand(CapsuleMode.Drop);
        }
    }

    protected override void OnDragOver(DragEventArgs drgevent)
    {
        base.OnDragOver(drgevent);
        drgevent.Effect = !ShellFileDrop.InternalDragActive && drgevent.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        _dragLeaveTimer.Stop();
        _dragLeaveTimer.Start();
    }

    protected override void OnDragDrop(DragEventArgs drgevent)
    {
        base.OnDragDrop(drgevent);
        _dragLeaveTimer.Stop();
        if (ShellFileDrop.InternalDragActive || drgevent.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            Collapse();
            return;
        }

        AppLog.Info($"悬浮舱收到拖入：{paths.Length} 项");
        _starsExpanded = paths.Any(File.Exists);
        BeginInvoke(() =>
        {
            _host.HandleDroppedPaths(paths);
            Expand(CapsuleMode.Peek);
        });
    }

    private void FinishWindowMove()
    {
        var cursor = Cursor.Position;
        var area = Screen.FromPoint(cursor).WorkingArea;
        var bounds = ClampToScreen(Bounds);
        // 窗口靠近左右边缘，或鼠标直接顶到屏幕边缘，都算“贴边”
        var nearLeft = bounds.Left - area.Left <= S(EdgeSnapDistance) || cursor.X <= area.Left + S(3);
        var nearRight = area.Right - bounds.Right <= S(EdgeSnapDistance) || cursor.X >= area.Right - S(4);
        if (_mode == CapsuleMode.Peek && (nearLeft || nearRight))
        {
            // 展开面板直接拖到边缘：收成细条隐藏
            Bounds = bounds;
            DockToEdge(nearLeft);
            return;
        }

        if (_mode == CapsuleMode.Peek)
        {
            // 展开状态下拖动：面板留在原地，收起后回到面板的锚定角
            Bounds = bounds;
            var restW = S(RestW);
            var restH = S(RestH);
            _restBounds = ClampToScreen(new Rectangle(
                _anchorRight ? bounds.Right - restW : bounds.Left,
                _anchorBottom ? bounds.Bottom - restH : bounds.Top,
                restW,
                restH));
            _collapsedMode = CapsuleMode.Rest;
            _host.SaveCapsuleLocation(_restBounds.Location);
            ScheduleCollapseCheck();
            return;
        }

        if (_mode == CapsuleMode.Edge)
        {
            // 从细条拖出来：还原成卡片
            _restBounds = ClampToScreen(new Rectangle(bounds.Left - (S(RestW) - bounds.Width) / 2, bounds.Top - (S(RestH) - bounds.Height) / 2, S(RestW), S(RestH)));
        }
        else
        {
            _restBounds = new Rectangle(bounds.Location, new Size(S(RestW), S(RestH)));
        }

        if (nearLeft || nearRight)
        {
            _restBounds.X = nearLeft ? area.Left : area.Right - _restBounds.Width;
            SnapToEdge(nearLeft);
            AnimateTo(EdgeBounds(), null);
        }
        else
        {
            _mode = CapsuleMode.Rest;
            _collapsedMode = CapsuleMode.Rest;
            AnimateTo(_restBounds, null);
        }

        _host.SaveCapsuleLocation(_restBounds.Location);
    }

    /// <summary>贴到左/右边缘并收成细条；鼠标移到细条上会展开，移开又缩回去。</summary>
    private void DockToEdge(bool? left = null)
    {
        var expanded = _mode is CapsuleMode.Peek or CapsuleMode.Drop;
        var reference = expanded ? Bounds : _collapsedMode == CapsuleMode.Edge ? EdgeBounds() : _restBounds;
        var area = Screen.FromRectangle(reference).WorkingArea;
        var toLeft = left ?? reference.Left + reference.Width / 2 < area.Left + area.Width / 2;
        var restW = S(RestW);
        var restH = S(RestH);
        var y = expanded && _anchorBottom ? reference.Bottom - restH : reference.Top;
        _restBounds = ClampToScreen(new Rectangle(toLeft ? area.Left : area.Right - restW, y, restW, restH));
        _pinned = false;
        _pinnedByTray = false;
        _chipsExpanded = false;
        _favoritesExpanded = false;
        SnapToEdge(toLeft);
        _collapseTimer.Stop();
        _expandTimer.Stop();
        AnimateTo(EdgeBounds(), null);
        _host.SaveCapsuleLocation(_restBounds.Location);
    }

    /// <summary>取消贴边：回到离边缘稍远的位置，显示为卡片。</summary>
    private void UndockFromEdge()
    {
        var area = Screen.FromRectangle(_restBounds).WorkingArea;
        var restW = S(RestW);
        _restBounds = ClampToScreen(new Rectangle(
            _edgeOnLeft ? area.Left + S(EdgeSnapDistance + 16) : area.Right - restW - S(EdgeSnapDistance + 16),
            _restBounds.Top,
            restW,
            S(RestH)));
        _collapsedMode = CapsuleMode.Rest;
        if (_mode == CapsuleMode.Edge)
        {
            _mode = CapsuleMode.Rest;
            AnimateTo(_restBounds, null);
        }
        else if (_mode is CapsuleMode.Peek or CapsuleMode.Drop)
        {
            ComputeAnchors();
        }

        _host.SaveCapsuleLocation(_restBounds.Location);
    }

    private void AddEdgeMenuItem(ContextMenuStrip menu)
    {
        if (_collapsedMode == CapsuleMode.Edge)
        {
            menu.Items.Add("取消贴边", null, (_, _) => UndockFromEdge());
        }
        else
        {
            menu.Items.Add("贴边隐藏", null, (_, _) => DockToEdge());
        }
    }

    private void ScheduleCollapseCheck()
    {
        if (_mode is CapsuleMode.Peek or CapsuleMode.Toast)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    private void TryAutoCollapse()
    {
        _collapseTimer.Stop();
        if (_mode is not (CapsuleMode.Peek or CapsuleMode.Toast))
        {
            return;
        }

        if (_pinned || _menuOpen || _holdOpen > 0 || _animationTimer.Enabled || IsCursorInside())
        {
            if (!_pinned)
            {
                _collapseTimer.Start();
            }

            return;
        }

        if (_mode == CapsuleMode.Toast)
        {
            return;
        }

        Collapse();
    }

    private bool IsCursorInside()
    {
        var bounds = Bounds;
        bounds.Inflate(S(4), S(4));
        return bounds.Contains(Cursor.Position);
    }

    // =====================================================================
    // 点击与菜单
    // =====================================================================

    private void RunHit(HitItem hit)
    {
        switch (hit.Kind)
        {
            case HitKind.Body:
                Expand(CapsuleMode.Peek);
                break;
            case HitKind.OpenLatest:
                _host.OpenLatestFolder();
                break;
            case HitKind.Pin:
                _pinned = !_pinned;
                _pinnedByTray = false;
                Invalidate();
                ScheduleCollapseCheck();
                break;
            case HitKind.Authorize:
                _host.AuthorizeMonitoring();
                break;
            case HitKind.RecentFile:
            case HitKind.StarFile:
                if (hit.Path is not null)
                {
                    _host.RunFileCommandForPath(hit.Path, FileCommand.Open);
                }

                break;
            case HitKind.ActiveChip:
            case HitKind.Favorite:
                if (hit.Path is not null)
                {
                    _host.OpenActivityFolderPath(hit.Path);
                }

                break;
            case HitKind.ActiveMore:
                _chipsExpanded = !_chipsExpanded;
                ResizeForContent();
                break;
            case HitKind.FavoriteMore:
                _favoritesExpanded = !_favoritesExpanded;
                ResizeForContent();
                break;
            case HitKind.StarsToggle:
                _starsExpanded = !_starsExpanded;
                ResizeForContent();
                break;
            case HitKind.Pause:
                _host.ToggleMonitoring();
                break;
            case HitKind.Settings:
                _host.OpenSettingsDialog();
                break;
            case HitKind.More:
                ShowMoreMenu(hit.Rect);
                break;
        }
    }

    private void ShowContextMenu(HitItem? hit, Point location)
    {
        var kind = hit?.Kind;
        var path = hit?.Path;
        var latest = _data.LatestFolderPath;
        ContextMenuStrip? menu = null;
        if (path is not null && kind is HitKind.RecentFile or HitKind.StarFile)
        {
            menu = _host.BuildFileMenu(path);
        }
        else if (path is not null && kind == HitKind.ActiveChip)
        {
            menu = _host.BuildFolderMenu(path, isFavorite: false);
        }
        else if (path is not null && kind == HitKind.Favorite)
        {
            menu = _host.BuildFolderMenu(path, isFavorite: true);
        }
        else if (kind is HitKind.Body or HitKind.Header)
        {
            if (latest is not null)
            {
                menu = _host.BuildFolderMenu(latest, isFavorite: false);
                menu.Items.Add(new ToolStripSeparator());
            }
            else
            {
                menu = DarkMenuRenderer.CreateMenu();
            }

            AddEdgeMenuItem(menu);
        }

        if (menu is null)
        {
            return;
        }

        ShowOwnedMenu(menu, location);
    }

    private void ShowMoreMenu(Rectangle anchor)
    {
        var menu = DarkMenuRenderer.CreateMenu();
        menu.Items.Add("全部活动记录", null, (_, _) => _host.ShowHistoryDialog());
        menu.Items.Add("添加收藏文件夹…", null, (_, _) => _host.AddFavoriteFolderDialog());
        menu.Items.Add("清空最近记录", null, (_, _) => _host.ClearRecent());
        menu.Items.Add("诊断信息", null, (_, _) => _host.ShowDiagnosticsDialog());
        AddEdgeMenuItem(menu);
        if (!Elevation.IsElevated)
        {
            menu.Items.Add("授权后台监视", null, (_, _) => _host.AuthorizeMonitoring());
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出 DropSpot", null, (_, _) => _host.ExitApplication()) { Tag = "danger" });
        ShowOwnedMenu(menu, new Point(anchor.Right, anchor.Top), ToolStripDropDownDirection.AboveLeft);
    }

    private void ShowOwnedMenu(ContextMenuStrip menu, Point location, ToolStripDropDownDirection direction = ToolStripDropDownDirection.Default)
    {
        _menuOpen = true;
        _collapseTimer.Stop();
        Invalidate();
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (IsDisposed || !IsHandleCreated)
            {
                menu.Dispose();
                return;
            }

            Invalidate();
            BeginInvoke(() => menu.Dispose());
            ScheduleCollapseCheck();
        };
        if (direction == ToolStripDropDownDirection.Default)
        {
            menu.Show(this, location);
        }
        else
        {
            menu.Show(this, location, direction);
        }
    }

    // =====================================================================
    // 布局（同一套代码既算命中区域又负责绘制）
    // =====================================================================

    private HitItem? HitTest(Point point)
    {
        for (var index = _hits.Count - 1; index >= 0; index--)
        {
            if (_hits[index].Rect.Contains(point))
            {
                return _hits[index];
            }
        }

        return null;
    }

    private void RebuildHits()
    {
        _hits.Clear();
        if (!_contentVisible)
        {
            return;
        }

        Render(null);
    }

    private int MeasurePeekHeight()
    {
        var saved = new List<HitItem>(_hits);
        var height = LayoutPeek(null, S(PeekW), measureOnly: true);
        _hits.Clear();
        _hits.AddRange(saved);
        return height;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_layered)
        {
            // 分层窗口的内容由 RenderLayered 推送，WM_PAINT 不参与显示
            return;
        }

        PaintSurface(e.Graphics);
    }

    private void PaintSurface(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = _layered ? TextRenderingHint.AntiAliasGridFit : TextRenderingHint.ClearTypeGridFit;
        var baseColor = _mode == CapsuleMode.Drop ? Color.FromArgb(24, 30, 24) : PanelColor;
        var borderColor = _mode switch
        {
            CapsuleMode.Drop => Color.FromArgb(107, 90, 42),
            CapsuleMode.Toast => Color.FromArgb(46, 107, 74),
            _ => BorderColor
        };
        if (_layered)
        {
            var alpha = (int)Math.Round(_opacity * 2.55);
            var radius = CornerRadius();
            using var path = RoundedRectF(new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), radius);
            using (var fill = new SolidBrush(Color.FromArgb(alpha, baseColor)))
            {
                g.FillPath(fill, path);
            }

            // 顶部一道很淡的高光，让玻璃感更明显
            // 注意：LinearGradientBrush 默认平铺，超出渐变区域会重复成条纹，所以只在顶部裁剪区内填充
            var glowHeight = Math.Min(Height, Math.Max(2, S(28)));
            var glowRect = new Rectangle(0, 0, Width, glowHeight);
            using (var glow = new LinearGradientBrush(new Rectangle(0, -1, Width, glowHeight + 2),
                       Color.FromArgb(22, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
            {
                var clip = g.Clip;
                g.SetClip(glowRect, CombineMode.Intersect);
                g.FillPath(glow, path);
                g.Clip = clip;
                clip.Dispose();
            }

            using var borderPen = new Pen(Color.FromArgb(Math.Min(255, alpha + 50), borderColor));
            g.DrawPath(borderPen, path);
        }
        else if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            g.Clear(baseColor);
            using var borderPen = new Pen(borderColor);
            using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius());
            g.DrawPath(borderPen, path);
        }
        else
        {
            g.Clear(baseColor);
            using var borderPen = new Pen(borderColor);
            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        }

        if (!_contentVisible)
        {
            return;
        }

        _hits.Clear();
        Render(g);
    }

    private void Render(Graphics? g)
    {
        switch (_mode)
        {
            case CapsuleMode.Rest:
                RenderRest(g);
                break;
            case CapsuleMode.Edge:
                RenderEdge(g);
                break;
            case CapsuleMode.Toast:
                RenderToast(g);
                break;
            case CapsuleMode.Drop:
                RenderDrop(g);
                break;
            case CapsuleMode.Peek:
                LayoutPeek(g, Width, measureOnly: false);
                break;
        }
    }

    private void RenderRest(Graphics? g)
    {
        var bounds = new Rectangle(0, 0, Width, Height);
        _hits.Add(new HitItem(bounds, HitKind.Body, 0, null, _data.LatestFolderPath ?? "DropSpot"));
        if (g is null)
        {
            return;
        }

        var iconRect = new Rectangle((Width - S(48)) / 2, S(14), S(48), S(40));
        DrawFolderIcon(g, iconRect);
        DrawStatusDot(g, new Point(Width - S(14), S(14)), S(4));
        if (_data.StarredFiles.Count > 0)
        {
            var text = $"★{_data.StarredFiles.Count}";
            var size = TextRenderer.MeasureText(text, _fontTiny);
            var badge = new Rectangle(iconRect.Right - S(10), iconRect.Bottom - S(12), Math.Max(S(22), size.Width + S(6)), S(17));
            using var badgePath = RoundedRect(badge, badge.Height / 2);
            using var badgeFill = new SolidBrush(Surface(PanelColor));
            using var badgePen = new Pen(Color.FromArgb(107, 90, 42));
            g.FillPath(badgeFill, badgePath);
            g.DrawPath(badgePen, badgePath);
            DrawText(g, text, _fontTiny, Gold, badge, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        DrawText(g, _data.LatestFolderName, _fontSmall, Theme.Text,
            new Rectangle(S(6), S(62), Width - S(12), S(22)),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void RenderEdge(Graphics? g)
    {
        _hits.Add(new HitItem(new Rectangle(0, 0, Width, Height), HitKind.Body, 0, null, "DropSpot"));
        if (g is null)
        {
            return;
        }

        DrawStatusDot(g, new Point(Width / 2, S(16)), S(3));
        using var bar = new SolidBrush(Color.FromArgb(58, 76, 102));
        using var barPath = RoundedRect(new Rectangle(Width / 2 - S(2), S(26), S(3), S(24)), S(2));
        g.FillPath(bar, barPath);
    }

    private void RenderToast(Graphics? g)
    {
        _hits.Add(new HitItem(new Rectangle(0, 0, Width, Height), HitKind.Body, 0, null, _data.LatestFolderPath));
        if (g is null)
        {
            return;
        }

        var iconRect = new Rectangle(S(16), (Height - S(40)) / 2, S(48), S(40));
        DrawFolderIcon(g, iconRect);
        DrawStatusDot(g, new Point(iconRect.Right - S(2), iconRect.Top + S(2)), S(5));
        var x = iconRect.Right + S(14);
        var width = Width - x - S(14);
        DrawText(g, "新文件写入", _fontTiny, Green, new Rectangle(x, S(20), width, S(16)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        DrawText(g, _toastFile ?? string.Empty, _fontBodyBold, Theme.Text, new Rectangle(x, S(36), width, S(22)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        DrawText(g, "→ " + (_toastFolder ?? string.Empty), _fontSmall, Muted, new Rectangle(x, S(58), width, S(18)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void RenderDrop(Graphics? g)
    {
        if (g is null)
        {
            return;
        }

        var zone = new Rectangle(S(14), S(14), Width - S(28), Height - S(28));
        using (var fill = new SolidBrush(Color.FromArgb(18, 242, 196, 92)))
        using (var pen = new Pen(Gold, 1.5F * _s) { DashStyle = DashStyle.Dash })
        using (var path = RoundedRect(zone, S(12)))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        DrawText(g, "★", _fontStar, Gold, new Rectangle(zone.X, zone.Y + S(18), zone.Width, S(40)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        DrawText(g, "松开即可收藏", _fontTitle, Gold, new Rectangle(zone.X, zone.Y + S(62), zone.Width, S(24)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        DrawText(g, "文件归入所在的收藏夹 · 文件夹直接收藏", _fontSmall, Color.FromArgb(169, 182, 198), new Rectangle(zone.X, zone.Y + S(90), zone.Width, S(20)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    /// <summary>展开面板布局：返回内容总高度。g 为 null 时只计算命中区域 / 高度。</summary>
    private int LayoutPeek(Graphics? g, int width, bool measureOnly)
    {
        var y = S(12);
        var pad = S(14);

        // ---- 顶部：最新活跃文件夹 ----
        var header = new Rectangle(0, 0, width, S(52));
        _hits.Add(new HitItem(header, HitKind.Header, 0, null, _data.LatestFolderPath));
        var pinRect = new Rectangle(width - S(40), y, S(30), S(30));
        var openRect = new Rectangle(pinRect.Left - S(32), y, S(30), S(30));
        _hits.Add(new HitItem(openRect, HitKind.OpenLatest, 0, null, "打开文件夹并选中最新文件"));
        _hits.Add(new HitItem(pinRect, HitKind.Pin, 0, null, _pinned ? "取消固定（移开后自动收起）" : "固定展开"));
        if (g is not null)
        {
            DrawFolderIcon(g, new Rectangle(pad, y + S(1), S(32), S(27)));
            var textX = pad + S(42);
            var textW = openRect.Left - textX - S(4);
            DrawText(g, _data.LatestFolderName, _fontTitle, Theme.Text, new Rectangle(textX, y - S(2), textW, S(20)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            DrawStatusDot(g, new Point(textX + S(3), y + S(26)), S(3));
            var sub = _data.LatestTime is DateTime time
                ? $"最近活跃 · {TimeText.Relative(time)} · {_data.LatestCount} 次"
                : "暂无活跃文件夹";
            DrawText(g, sub, _fontTiny, Muted, new Rectangle(textX + S(10), y + S(18), textW - S(10), S(16)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            DrawIconButton(g, openRect, "", Muted, false);
            DrawIconButton(g, pinRect, "", _pinned ? Green : Muted, _pinned);
        }

        y = S(52);

        // ---- 授权提示（需要时） ----
        if (_data.NeedsAuthorization)
        {
            var banner = new Rectangle(S(10), y, width - S(20), S(36));
            var button = new Rectangle(banner.Right - S(62), banner.Top + S(5), S(56), S(26));
            _hits.Add(new HitItem(button, HitKind.Authorize, 0, null, "确认一次管理员授权，之后开机不再询问"));
            if (g is not null)
            {
                FillRounded(g, banner, S(9), Color.FromArgb(58, 44, 12));
                DrawText(g, _data.AuthorizationText, _fontSmall, Color.FromArgb(253, 224, 138), new Rectangle(banner.X + S(10), banner.Y, banner.Width - S(80), banner.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                FillRounded(g, button, S(7), IsHover(HitKind.Authorize) ? Color.FromArgb(150, 110, 26) : Color.FromArgb(120, 88, 20));
                DrawText(g, _data.AuthorizationButton, _fontSmall, Color.White, button, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            y += S(42);
        }

        // ---- 最新文件（使用频率最高：最大、最靠前） ----
        if (_data.RecentFiles.Count == 0)
        {
            g?.Let(gr => DrawText(gr, "还没有最近文件", _fontSmall, Faint, new Rectangle(pad, y, width - pad * 2, S(36)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter));
            y += S(36);
        }

        for (var index = 0; index < _data.RecentFiles.Count; index++)
        {
            var file = _data.RecentFiles[index];
            var row = new Rectangle(S(6), y, width - S(12), S(42));
            var item = new HitItem(row, HitKind.RecentFile, index, file.Path, $"{file.Path}\r\n单击打开 · 按住拖出 · 右键更多");
            _hits.Add(item);
            if (g is not null)
            {
                if (IsHover(HitKind.RecentFile, index))
                {
                    FillRounded(g, row, S(10), HoverColor);
                }

                var badge = new Rectangle(row.X + S(8), row.Y + S(5), S(32), S(32));
                var color = ExtColor(file.Ext);
                FillRounded(g, badge, S(8), Color.FromArgb(30, color));
                DrawText(g, file.Ext, _fontBadge, color, badge, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                var textX = badge.Right + S(10);
                var textW = row.Right - textX - S(8);
                DrawText(g, file.Name, _fontBodyBold, file.Exists ? Theme.Text : Theme.Dim, new Rectangle(textX, row.Y + S(3), textW, S(20)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                DrawText(g, $"{TimeText.Relative(file.Time)} · {file.FolderName}", _fontTiny, Faint, new Rectangle(textX, row.Y + S(22), textW, S(16)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            y += S(42);
        }

        y += S(6);
        if (g is not null)
        {
            using var divider = new Pen(Color.FromArgb(30, 42, 58));
            g.DrawLine(divider, pad, y, width - pad, y);
        }

        y += S(7);

        // ---- 活跃文件夹：一行小标签，“+N”展开 ----
        if (_data.ActiveFolders.Count > 0)
        {
            y = LayoutChips(g, width, y);
        }

        // ---- 收藏夹：一行图标，“+N”展开 ----
        y = LayoutFavorites(g, width, y + S(4));

        // ---- ★ 收藏文件：默认折叠 ----
        if (_data.StarredFiles.Count > 0)
        {
            var toggle = new Rectangle(S(6), y, width - S(12), S(32));
            _hits.Add(new HitItem(toggle, HitKind.StarsToggle, 0, null, null));
            if (g is not null)
            {
                if (IsHover(HitKind.StarsToggle))
                {
                    FillRounded(g, toggle, S(8), HoverColor);
                }

                DrawText(g, $"★ 收藏文件  {_data.StarredFiles.Count}", _fontSmall, Gold, new Rectangle(toggle.X + S(8), toggle.Y, toggle.Width - S(80), toggle.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                DrawText(g, _starsExpanded ? "收起 ▴" : "展开 ▾", _fontTiny, Faint, new Rectangle(toggle.Right - S(70), toggle.Y, S(62), toggle.Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }

            y += S(32);
            if (_starsExpanded)
            {
                for (var index = 0; index < _data.StarredFiles.Count && index < 8; index++)
                {
                    var file = _data.StarredFiles[index];
                    var row = new Rectangle(S(6), y, width - S(12), S(30));
                    _hits.Add(new HitItem(row, HitKind.StarFile, index, file.Path, $"{file.Path}\r\n单击打开 · 按住拖出 · 右键更多"));
                    if (g is not null)
                    {
                        if (IsHover(HitKind.StarFile, index))
                        {
                            FillRounded(g, row, S(8), HoverColor);
                        }

                        var color = ExtColor(file.Ext);
                        DrawText(g, file.Ext, _fontBadge, color, new Rectangle(row.X + S(12), row.Y, S(32), row.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                        DrawText(g, file.Name, _fontSmall, file.Exists ? Theme.Text : Theme.Dim, new Rectangle(row.X + S(48), row.Y, row.Width - S(128), row.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                        DrawText(g, file.FolderName, _fontTiny, Faint, new Rectangle(row.Right - S(78), row.Y, S(70), row.Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    }

                    y += S(30);
                }
            }
        }

        y += S(6);

        // ---- 底栏 ----
        var footerTop = measureOnly ? y : Math.Max(y, Height - S(38));
        var footer = new Rectangle(0, footerTop, width, S(38));
        var moreRect = new Rectangle(width - S(36), footer.Top + S(5), S(28), S(28));
        var settingsRect = new Rectangle(moreRect.Left - S(30), moreRect.Top, S(28), S(28));
        var pauseRect = new Rectangle(settingsRect.Left - S(30), moreRect.Top, S(28), S(28));
        _hits.Add(new HitItem(new Rectangle(0, footer.Top, pauseRect.Left - S(4), footer.Height), HitKind.Header, 1, null, null));
        _hits.Add(new HitItem(pauseRect, HitKind.Pause, 0, null, _data.Monitoring ? "暂停监视" : "开始监视"));
        _hits.Add(new HitItem(settingsRect, HitKind.Settings, 0, null, "设置"));
        _hits.Add(new HitItem(moreRect, HitKind.More, 0, null, "更多"));
        if (g is not null)
        {
            using var line = new Pen(Color.FromArgb(30, 42, 58));
            g.DrawLine(line, 0, footer.Top, width, footer.Top);
            var status = _message ?? _data.StatusText;
            var statusColor = _data.NeedsAuthorization ? Color.FromArgb(229, 181, 58) : _data.Monitoring ? Green : Theme.Dim;
            using (var dot = new SolidBrush(statusColor))
            {
                g.FillEllipse(dot, pad, footer.Top + S(16), S(6), S(6));
            }

            DrawText(g, status, _fontTiny, _message is null ? Muted : Theme.Text, new Rectangle(pad + S(12), footer.Top, pauseRect.Left - pad - S(16), footer.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            DrawIconButton(g, pauseRect, _data.Monitoring ? "" : "", Muted, false);
            DrawIconButton(g, settingsRect, "", Muted, false);
            DrawIconButton(g, moreRect, "", Muted, _menuOpen);
        }

        return footerTop + S(38);
    }

    private int LayoutChips(Graphics? g, int width, int y)
    {
        var pad = S(14);
        var labelW = S(30);
        var left = pad + labelW;
        var right = width - S(12);
        var chipH = S(28);
        var gap = S(6);
        if (g is not null)
        {
            DrawText(g, "活跃", _fontTiny, Faint, new Rectangle(pad, y, labelW, chipH), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        var folders = _data.ActiveFolders;
        var widths = folders.Select(folder => Math.Min(S(108), TextRenderer.MeasureText(folder.Name, _fontSmall).Width + S(folder.Fresh ? 26 : 18))).ToArray();
        var moreText = _chipsExpanded ? "收起" : string.Empty;
        var visible = folders.Count;
        if (!_chipsExpanded)
        {
            // 放得下几个就显示几个，剩下的收进 “+N”
            var available = right - left;
            var used = 0;
            visible = 0;
            for (var index = 0; index < folders.Count; index++)
            {
                var remaining = folders.Count - index - 1;
                var reserve = remaining > 0 ? TextRenderer.MeasureText($"+{remaining}", _fontSmall).Width + S(18) + gap : 0;
                if (used + widths[index] + reserve > available)
                {
                    break;
                }

                used += widths[index] + gap;
                visible++;
            }

            visible = Math.Max(1, visible);
            moreText = visible < folders.Count ? $"+{folders.Count - visible}" : string.Empty;
        }

        var x = left;
        var rowY = y;
        for (var index = 0; index < visible; index++)
        {
            if (x + widths[index] > right && x > left)
            {
                x = left;
                rowY += chipH + gap;
            }

            var chip = new Rectangle(x, rowY, Math.Min(widths[index], right - x), chipH);
            var folder = folders[index];
            _hits.Add(new HitItem(chip, HitKind.ActiveChip, index, folder.Path, $"{folder.Path}\r\n单击打开并选中最新文件 · 右键更多"));
            if (g is not null)
            {
                FillRounded(g, chip, chipH / 2, IsHover(HitKind.ActiveChip, index) ? HoverColor : TileColor, TileBorder);
                var textX = chip.X + S(9);
                if (folder.Fresh)
                {
                    using var dot = new SolidBrush(Green);
                    g.FillEllipse(dot, textX, chip.Y + chipH / 2 - S(3), S(5), S(5));
                    textX += S(9);
                }

                DrawText(g, folder.Name, _fontSmall, Theme.Text, new Rectangle(textX, chip.Y, chip.Right - textX - S(8), chipH), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            x += chip.Width + gap;
        }

        if (moreText.Length > 0)
        {
            var moreW = TextRenderer.MeasureText(moreText, _fontSmall).Width + S(18);
            if (x + moreW > right && x > left)
            {
                x = left;
                rowY += chipH + gap;
            }

            var more = new Rectangle(x, rowY, moreW, chipH);
            _hits.Add(new HitItem(more, HitKind.ActiveMore, 0, null, _chipsExpanded ? "收起" : "显示全部活跃文件夹"));
            if (g is not null)
            {
                using var path = RoundedRect(more, chipH / 2);
                using var pen = new Pen(Color.FromArgb(58, 76, 102)) { DashStyle = DashStyle.Dash };
                if (IsHover(HitKind.ActiveMore))
                {
                    using var fill = new SolidBrush(Surface(HoverColor));
                    g.FillPath(fill, path);
                }

                g.DrawPath(pen, path);
                DrawText(g, moreText, _fontSmall, Color.FromArgb(169, 182, 198), more, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        return rowY + chipH + S(8);
    }

    private int LayoutFavorites(Graphics? g, int width, int y)
    {
        var pad = S(14);
        var labelW = S(30);
        var left = pad + labelW;
        var right = width - S(12);
        var columns = 5;
        var gap = S(4);
        var cellW = (right - left - gap * (columns - 1)) / columns;
        var cellH = S(50);
        if (g is not null)
        {
            DrawText(g, "收藏", _fontTiny, Faint, new Rectangle(pad, y, labelW, cellH), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        var favorites = _data.Favorites;
        var showMore = favorites.Count > columns;
        var shown = showMore && !_favoritesExpanded ? columns - 1 : favorites.Count;
        var cells = shown + (showMore ? 1 : 0);
        if (favorites.Count == 0)
        {
            var hint = new Rectangle(left, y, right - left, cellH);
            _hits.Add(new HitItem(hint, HitKind.More, 0, null, "把文件夹拖到悬浮舱上，或在“⋯”里添加收藏"));
            if (g is not null)
            {
                FillRounded(g, hint, S(9), Color.Transparent, Color.FromArgb(42, 54, 72));
                DrawText(g, "拖入文件夹即可收藏", _fontSmall, Faint, hint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            return y + cellH + S(6);
        }

        for (var index = 0; index < cells; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var cell = new Rectangle(left + column * (cellW + gap), y + row * (cellH + gap), cellW, cellH);
            if (index < shown)
            {
                var favorite = favorites[index];
                _hits.Add(new HitItem(cell, HitKind.Favorite, index, favorite.Path, $"{favorite.Path}\r\n单击打开 · 右键更多"));
                if (g is not null)
                {
                    if (IsHover(HitKind.Favorite, index))
                    {
                        FillRounded(g, cell, S(9), HoverColor);
                    }

                    DrawFolderIcon(g, new Rectangle(cell.X + (cell.Width - S(26)) / 2, cell.Y + S(6), S(26), S(22)));
                    DrawText(g, favorite.Name, _fontTiny, Color.FromArgb(196, 207, 219), new Rectangle(cell.X + S(2), cell.Y + S(30), cell.Width - S(4), S(16)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    if (favorite.Fresh)
                    {
                        using var dot = new SolidBrush(Green);
                        g.FillEllipse(dot, cell.Right - S(10), cell.Y + S(5), S(5), S(5));
                    }
                }
            }
            else
            {
                _hits.Add(new HitItem(cell, HitKind.FavoriteMore, 0, null, _favoritesExpanded ? "收起" : "显示全部收藏夹"));
                if (g is not null)
                {
                    using var path = RoundedRect(cell, S(9));
                    using var pen = new Pen(Color.FromArgb(58, 76, 102)) { DashStyle = DashStyle.Dash };
                    if (IsHover(HitKind.FavoriteMore))
                    {
                        using var fill = new SolidBrush(Surface(HoverColor));
                        g.FillPath(fill, path);
                    }

                    g.DrawPath(pen, path);
                    var big = _favoritesExpanded ? "收起" : $"+{favorites.Count - shown}";
                    DrawText(g, big, _fontBodyBold, Theme.Text, new Rectangle(cell.X, cell.Y + S(6), cell.Width, S(22)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    if (!_favoritesExpanded)
                    {
                        DrawText(g, "更多", _fontTiny, Muted, new Rectangle(cell.X, cell.Y + S(28), cell.Width, S(16)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                }
            }
        }

        var rows = (cells + columns - 1) / columns;
        return y + rows * cellH + (rows - 1) * gap + S(6);
    }

    // =====================================================================
    // 绘制工具
    // =====================================================================

    private int S(int value) => (int)Math.Round(value * _s);

    private bool IsHover(HitKind kind, int index = 0) => _hover is not null && _hover.Kind == kind && _hover.Index == index;

    private void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds, TextFormatFlags flags)
    {
        if (!_layered)
        {
            TextRenderer.DrawText(g, text, font, bounds, color, flags | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            return;
        }

        // GDI 文字会把透明位图的 alpha 写成 0（字变成洞），分层模式改用 GDI+ 灰度抗锯齿
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.None,
            Trimming = (flags & TextFormatFlags.EndEllipsis) != 0 ? StringTrimming.EllipsisCharacter : StringTrimming.None,
            Alignment = (flags & TextFormatFlags.HorizontalCenter) != 0 ? StringAlignment.Center
                : (flags & TextFormatFlags.Right) != 0 ? StringAlignment.Far
                : StringAlignment.Near,
            LineAlignment = (flags & TextFormatFlags.VerticalCenter) != 0 ? StringAlignment.Center
                : (flags & TextFormatFlags.Bottom) != 0 ? StringAlignment.Far
                : StringAlignment.Near
        };
        var pad = (flags & TextFormatFlags.NoPadding) != 0 ? 0 : Math.Max(0, font.Height / 6 - 2);
        var layout = new RectangleF(bounds.X + pad, bounds.Y, Math.Max(1, bounds.Width - pad * 2), bounds.Height);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, layout, format);
    }

    private void DrawIconButton(Graphics g, Rectangle rect, string glyph, Color color, bool active)
    {
        var hovered = _hover is not null && _hover.Rect == rect;
        if (hovered || active)
        {
            FillRounded(g, rect, S(7), active ? Color.FromArgb(40, 61, 220, 132) : HoverColor);
        }

        DrawText(g, glyph, _fontIcon, hovered ? Theme.Text : color, rect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawStatusDot(Graphics g, Point center, int radius)
    {
        var color = _data.NeedsAuthorization ? Color.FromArgb(229, 181, 58) : _data.Monitoring ? Green : Theme.Dim;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
    }

    private void FillRounded(Graphics g, Rectangle rect, int radius, Color fill, Color? border = null)
    {
        using var path = RoundedRect(rect, radius);
        fill = Surface(fill);
        if (fill.A > 0)
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }

        if (border is Color borderColor)
        {
            using var pen = new Pen(borderColor);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>双色文件夹图标：深金色后片 + 白纸 + 亮金色前片。</summary>
    private static void DrawFolderIcon(Graphics g, Rectangle rect)
    {
        var sx = rect.Width / 48F;
        var sy = rect.Height / 40F;
        RectangleF R(float x, float y, float w, float h) => new(rect.X + x * sx, rect.Y + y * sy, w * sx, h * sy);
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var back = new GraphicsPath())
        {
            back.AddPath(RoundedRectF(R(4, 2, 40, 36), 4 * sx), false);
            using var brush = new SolidBrush(GoldDark);
            g.FillPath(brush, back);
        }

        using (var paper = new SolidBrush(Color.FromArgb(230, 244, 241, 234)))
        using (var paperPath = RoundedRectF(R(8, 9, 32, 20), 2 * sx))
        {
            g.FillPath(paper, paperPath);
        }

        using (var front = RoundedRectF(R(4, 11, 40, 27), 4 * sx))
        using (var brush = new SolidBrush(Gold))
        {
            g.FillPath(brush, front);
        }

        using (var shine = new SolidBrush(Color.FromArgb(190, 255, 227, 160)))
        using (var shinePath = RoundedRectF(R(8, 14.5F, 32, 1.6F), 0.8F * sx))
        {
            g.FillPath(shine, shinePath);
        }

        g.Restore(state);
    }

    private static Color ExtColor(string ext) => ext switch
    {
        "MP4" or "MOV" or "MKV" or "AVI" or "WEBM" => Color.FromArgb(201, 167, 255),
        "PNG" or "JPG" or "JPEG" or "GIF" or "WEBP" or "BMP" or "SVG" or "HEIC" => Color.FromArgb(242, 196, 92),
        "PDF" => Color.FromArgb(245, 158, 139),
        "DOC" or "DOCX" or "PSD" or "AI" or "PPT" or "PPTX" => Color.FromArgb(110, 168, 254),
        "XLS" or "XLSX" or "CSV" => Color.FromArgb(143, 217, 176),
        "ZIP" or "RAR" or "7Z" => Color.FromArgb(169, 182, 198),
        _ => Color.FromArgb(169, 182, 198)
    };

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        return RoundedRectF(rect, radius);
    }

    private static GraphicsPath RoundedRectF(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0.5F)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _s = e.DeviceDpiNew / 96F;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _s = DeviceDpi / 96F;
        ApplyShape();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var timer in new[] { _expandTimer, _collapseTimer, _toastTimer, _dragLeaveTimer, _animationTimer, _clockTimer, _messageTimer })
            {
                timer.Dispose();
            }

            _toolTip.Dispose();
            foreach (var font in new[] { _fontTitle, _fontBody, _fontBodyBold, _fontSmall, _fontTiny, _fontBadge, _fontIcon, _fontStar })
            {
                font.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private enum HitKind
    {
        Body,
        Header,
        OpenLatest,
        Pin,
        Authorize,
        RecentFile,
        ActiveChip,
        ActiveMore,
        Favorite,
        FavoriteMore,
        StarsToggle,
        StarFile,
        Pause,
        Settings,
        More
    }

    private sealed record HitItem(Rectangle Rect, HitKind Kind, int Index, string? Path, string? Tip);

    private sealed class Releaser : IDisposable
    {
        private Action? _release;

        public Releaser(Action release)
        {
            _release = release;
        }

        public void Dispose()
        {
            _release?.Invoke();
            _release = null;
        }
    }
}

internal static class GraphicsExtensions
{
    /// <summary>对可空对象执行操作的小工具（让布局代码在只测量时跳过绘制）。</summary>
    public static void Let(this Graphics graphics, Action<Graphics> action) => action(graphics);
}

/// <summary>UpdateLayeredWindow：把带 alpha 的位图推送到分层窗口。</summary>
internal static class LayeredNative
{
    private const int UlwAlpha = 0x02;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;

    public static void Push(Form form, Bitmap bitmap)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var hBitmap = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;
        try
        {
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = SelectObject(memoryDc, hBitmap);
            var size = new NativeSize(bitmap.Width, bitmap.Height);
            var source = new NativePoint(0, 0);
            var destination = new NativePoint(form.Left, form.Top);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };
            UpdateLayeredWindow(form.Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
            {
                SelectObject(memoryDc, oldBitmap);
            }

            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }

            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Cx;
        public int Cy;

        public NativeSize(int cx, int cy)
        {
            Cx = cx;
            Cy = cy;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize,
        IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}
