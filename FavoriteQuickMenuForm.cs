namespace DiskWriteWatcher;

internal sealed class FavoriteQuickMenuForm : Form
{
    private const int ItemWidth = 101;
    private const int ItemHeight = 70;
    private const int ItemGap = 6;
    private readonly Action<FavoriteFolder> _openFavorite;
    private readonly Action<FavoriteFolder, Rectangle> _showInfo;
    private readonly Action _hideInfo;
    private readonly Action _scheduleCollapse;
    private readonly Action<FavoriteFolder> _prepareContextMenu;
    private readonly ContextMenuStrip _contextMenu;
    private readonly List<FavoriteQuickItem> _items = new();
    private IReadOnlyList<FavoriteFolder> _favorites = Array.Empty<FavoriteFolder>();

    public FavoriteQuickMenuForm(
        Action<FavoriteFolder> openFavorite,
        Action<FavoriteFolder, Rectangle> showInfo,
        Action hideInfo,
        Action scheduleCollapse,
        Action<FavoriteFolder> prepareContextMenu,
        ContextMenuStrip contextMenu)
    {
        _openFavorite = openFavorite;
        _showInfo = showInfo;
        _hideInfo = hideInfo;
        _scheduleCollapse = scheduleCollapse;
        _prepareContextMenu = prepareContextMenu;
        _contextMenu = contextMenu;

        FormBorderStyle = FormBorderStyle.None;
        Text = "收藏快捷栏";
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Opacity = 0.70;
        Width = ItemWidth;
        Deactivate += (_, _) => _scheduleCollapse();
    }

    public void UpdateFavorites(IReadOnlyList<FavoriteFolder> favorites)
    {
        _favorites = favorites.Take(4).ToArray();
        if (Visible)
        {
            SyncItems();
        }
    }

    public void PrepareForShow()
    {
        SyncItems();
    }

    internal int ItemCount => _items.Count;

    internal bool TryGetVisibleContentBounds(out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!Visible || _items.Count == 0)
        {
            return false;
        }

        bounds = _items[0].RectangleToScreen(_items[0].ClientRectangle);
        for (var index = 1; index < _items.Count; index++)
        {
            bounds = Rectangle.Union(bounds, _items[index].RectangleToScreen(_items[index].ClientRectangle));
        }

        return bounds.Width > 0 && bounds.Height > 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeItems();
        }

        base.Dispose(disposing);
    }

    private void SyncItems()
    {
        if (_items.Count != _favorites.Count)
        {
            DisposeItems();
            Height = Math.Max(1, _favorites.Count * ItemHeight + Math.Max(0, _favorites.Count - 1) * ItemGap);
            for (var index = 0; index < _favorites.Count; index++)
            {
                var item = new FavoriteQuickItem(
                    _favorites[index],
                    rank: index + 2,
                    _openFavorite,
                    _showInfo,
                    _hideInfo,
                    _prepareContextMenu,
                    _contextMenu);
                _items.Add(item);
                Controls.Add(item);
            }
        }

        for (var index = 0; index < _items.Count; index++)
        {
            _items[index].Location = new Point(0, Height - (index + 1) * ItemHeight - index * ItemGap);
            _items[index].UpdateFavorite(_favorites[index], index + 2);
        }
    }

    private void DisposeItems()
    {
        foreach (var item in _items)
        {
            Controls.Remove(item);
            item.Dispose();
        }

        _items.Clear();
    }

    private sealed class FavoriteQuickItem : UserControl
    {
        private readonly Action<FavoriteFolder> _openFavorite;
        private readonly Action<FavoriteFolder, Rectangle> _showInfo;
        private readonly Action _hideInfo;
        private readonly PictureBox _icon = new();
        private readonly Label _name = new();
        private readonly Label _rank = new();
        private readonly Font _nameFont = new("Microsoft YaHei UI", 7.5F, FontStyle.Bold);
        private readonly Font _rankFont = new("Microsoft YaHei UI", 6.5F);
        private FavoriteFolder _favorite;

        public FavoriteQuickItem(
            FavoriteFolder favorite,
            int rank,
            Action<FavoriteFolder> openFavorite,
            Action<FavoriteFolder, Rectangle> showInfo,
            Action hideInfo,
            Action<FavoriteFolder> prepareContextMenu,
            ContextMenuStrip contextMenu)
        {
            _favorite = favorite;
            _openFavorite = openFavorite;
            _showInfo = showInfo;
            _hideInfo = hideInfo;

            Size = new Size(ItemWidth, ItemHeight);
            BackColor = Color.FromArgb(8, 12, 18);
            DoubleBuffered = true;
            Cursor = Cursors.Hand;

            _icon.Image = ShellIconProvider.FolderIcon();
            _icon.SizeMode = PictureBoxSizeMode.CenterImage;
            _icon.BackColor = Color.Transparent;
            _icon.Location = new Point(27, 2);
            _icon.Size = new Size(48, 44);
            Controls.Add(_icon);

            _name.AutoEllipsis = true;
            _name.Font = _nameFont;
            _name.ForeColor = Theme.Text;
            _name.BackColor = Color.Transparent;
            _name.TextAlign = ContentAlignment.MiddleCenter;
            _name.Location = new Point(8, 46);
            _name.Size = new Size(85, 18);
            Controls.Add(_name);

            _rank.Font = _rankFont;
            _rank.ForeColor = Theme.Dim;
            _rank.BackColor = Color.Transparent;
            _rank.Location = new Point(7, 5);
            _rank.Size = new Size(18, 14);
            Controls.Add(_rank);

            foreach (var control in Controls.Cast<Control>().Prepend(this))
            {
                control.Cursor = Cursors.Hand;
                control.ContextMenuStrip = contextMenu;
                control.Click += (_, _) => _openFavorite(_favorite);
                control.MouseEnter += (_, _) => ShowInfo();
                control.MouseLeave += (_, _) => ScheduleHideInfo();
                control.MouseDown += (_, e) =>
                {
                    if (e.Button == MouseButtons.Right)
                    {
                        prepareContextMenu(_favorite);
                    }
                };
            }

            SizeChanged += (_, _) => UpdateRoundedRegion();
            UpdateRoundedRegion();
            UpdateFavorite(favorite, rank);
        }

        public string FolderPath => _favorite.Path;

        public void UpdateFavorite(FavoriteFolder favorite, int rank)
        {
            _favorite = favorite;
            _name.Text = favorite.DisplayName;
            _rank.Text = rank.ToString();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _nameFont.Dispose();
                _rankFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private void ShowInfo()
        {
            _showInfo(_favorite, RectangleToScreen(ClientRectangle));
        }

        private void ScheduleHideInfo()
        {
            BeginInvoke(() =>
            {
                if (!ClientRectangle.Contains(PointToClient(Cursor.Position)))
                {
                    _hideInfo();
                }
            });
        }

        private void UpdateRoundedRegion()
        {
            Region?.Dispose();
            Region = FloatingFolderForm.CreateNativeRoundedRegion(ClientSize, 10);
        }
    }
}

internal sealed class FavoriteInfoPopupForm : Form
{
    private const int PopupWidth = 360;
    private const int PopupHeight = 110;
    private readonly Panel _surface = new();
    private readonly Label _name = new();
    private readonly Label _path = new();
    private readonly Label _time = new();
    private readonly Font _nameFont = new("Microsoft YaHei UI", 9F, FontStyle.Bold);
    private readonly Font _detailFont = new("Microsoft YaHei UI", 7.5F);
    public FavoriteInfoPopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        Text = "收藏信息";
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(PopupWidth, PopupHeight);
        BackColor = Color.Fuchsia;
        TransparencyKey = Color.Fuchsia;
        Opacity = 0.70;
        DoubleBuffered = true;

        _surface.Bounds = ClientRectangle;
        _surface.BackColor = Color.FromArgb(39, 52, 67);
        _surface.Region = FloatingFolderForm.CreateNativeRoundedRegion(_surface.ClientSize, 9);
        Controls.Add(_surface);

        _name.Font = _nameFont;
        _name.ForeColor = Color.White;
        _name.BackColor = Color.Transparent;
        _name.AutoEllipsis = true;
        _surface.Controls.Add(_name);

        _path.Font = _detailFont;
        _path.ForeColor = Color.FromArgb(198, 210, 224);
        _path.BackColor = Color.Transparent;
        _path.AutoEllipsis = false;
        _surface.Controls.Add(_path);

        _time.Font = _detailFont;
        _time.ForeColor = Color.FromArgb(242, 205, 112);
        _time.BackColor = Color.Transparent;
        _surface.Controls.Add(_time);
        SizeChanged += (_, _) => UpdateRoundedSurface();
        LayoutLabels();
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

    public void ShowFor(FavoriteFolder favorite, Rectangle anchor)
    {
        _name.Text = favorite.DisplayName;
        _path.Text = favorite.Path.Replace("\\", "\\\u200B");
        _time.Text = RelativeTime(favorite.LastActivity);

        var area = Screen.FromRectangle(anchor).WorkingArea;
        var left = anchor.Right + 12;
        var top = anchor.Top + (anchor.Height - Height) / 2;
        top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        Location = new Point(left, top);
        if (!Visible)
        {
            Show();
        }

        BringToFront();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _nameFont.Dispose();
            _detailFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void LayoutLabels()
    {
        _name.SetBounds(16, 10, PopupWidth - 32, 21);
        _path.SetBounds(16, 34, PopupWidth - 32, 42);
        _time.SetBounds(16, 84, PopupWidth - 32, 17);
    }

    private void UpdateRoundedSurface()
    {
        _surface.Bounds = ClientRectangle;
        _surface.Region?.Dispose();
        _surface.Region = FloatingFolderForm.CreateNativeRoundedRegion(_surface.ClientSize, 9);
    }

    private static string RelativeTime(DateTime time)
    {
        var span = DateTime.Now - time;
        if (span.TotalSeconds < 60)
        {
            return "刚刚更新";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{Math.Max(1, (int)span.TotalMinutes)} 分钟前更新";
        }

        if (span.TotalHours < 24)
        {
            return $"{Math.Max(1, (int)span.TotalHours)} 小时前更新";
        }

        return time.Date == DateTime.Today.AddDays(-1) ? "昨天更新" : $"{time:MM-dd} 更新";
    }
}
