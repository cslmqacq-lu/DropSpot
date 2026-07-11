namespace DropSpot;

public sealed class FavoriteFolderCard : UserControl
{
    private readonly Action<string> _openFolder;
    private readonly Action<FavoriteFolder> _removeFavorite;
    private readonly Action<string> _copyPath;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolTip _toolTip = new();
    private readonly PictureBox _icon = new();
    private readonly Label _name = new();
    private readonly Label _path = new();
    private readonly Label _time = new();
    private readonly Font _nameFont = new("Microsoft YaHei UI", 10F, FontStyle.Bold);
    private readonly Font _pathFont = new("Microsoft YaHei UI", 8F);
    private readonly Font _timeFont = new("Microsoft YaHei UI", 8F);
    private FavoriteFolder _favorite;
    private bool _isLatest;
    private bool _available;

    public FavoriteFolderCard(
        FavoriteFolder favorite,
        bool isLatest,
        int width,
        Action<string> openFolder,
        Action<FavoriteFolder> removeFavorite,
        Action<string> copyPath)
    {
        _favorite = favorite;
        _openFolder = openFolder;
        _removeFavorite = removeFavorite;
        _copyPath = copyPath;

        Height = 72;
        Margin = new Padding(0, 0, 0, 8);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;

        BuildUi();
        BuildMenu();
        UpdateFavorite(favorite, isLatest, width);
    }

    public string FolderPath => _favorite.Path;

    public void UpdateFavorite(FavoriteFolder favorite, bool isLatest, int width)
    {
        _favorite = favorite;
        _isLatest = isLatest;
        Width = width;

        var available = Directory.Exists(favorite.Path);
        _available = available;
        var cardColor = isLatest ? Theme.CardLatest : Theme.Card;
        BackColor = cardColor;
        _icon.BackColor = cardColor;
        _name.BackColor = cardColor;
        _path.BackColor = cardColor;
        _time.BackColor = cardColor;

        _name.Text = favorite.DisplayName;
        _path.Text = CompactPath(favorite.Path);
        RefreshRelativeTime();
        _name.ForeColor = available ? Theme.Text : Theme.Dim;
        _path.ForeColor = available ? Theme.Muted : Theme.Dim;
        _time.ForeColor = available ? Theme.Muted : Theme.Dim;

        var textWidth = Math.Max(130, width - 170);
        _name.Width = textWidth;
        _path.Width = textWidth;
        _time.Location = new Point(Math.Max(250, width - 92), 20);

        _toolTip.SetToolTip(this, favorite.Path);
        _toolTip.SetToolTip(_name, favorite.Path);
        _toolTip.SetToolTip(_path, favorite.Path);
        Invalidate();
    }

    public void RefreshRelativeTime()
    {
        _time.Text = _available ? RelativeTime(_favorite.LastActivity) : "不可用";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(_isLatest ? Theme.BorderStrong : Theme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        if (_isLatest)
        {
            using var latest = new Pen(Theme.Accent, 3F);
            e.Graphics.DrawLine(latest, 1, 1, 1, Height - 2);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _menu.Dispose();
            _toolTip.Dispose();
            _nameFont.Dispose();
            _pathFont.Dispose();
            _timeFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        _icon.Image = ShellIconProvider.FolderIcon();
        _icon.SizeMode = PictureBoxSizeMode.CenterImage;
        _icon.Location = new Point(12, 12);
        _icon.Size = new Size(48, 48);
        Controls.Add(_icon);

        _name.AutoEllipsis = true;
        _name.Font = _nameFont;
        _name.Location = new Point(72, 12);
        _name.Height = 23;
        Controls.Add(_name);

        _path.AutoEllipsis = true;
        _path.Font = _pathFont;
        _path.Location = new Point(72, 39);
        _path.Height = 20;
        Controls.Add(_path);

        _time.Font = _timeFont;
        _time.TextAlign = ContentAlignment.MiddleRight;
        _time.Size = new Size(68, 30);
        Controls.Add(_time);

        WireDoubleClick(this);
        WireDoubleClick(_icon);
        WireDoubleClick(_name);
        WireDoubleClick(_path);
        WireDoubleClick(_time);
    }

    private void BuildMenu()
    {
        _menu.Items.Add("打开文件夹", null, (_, _) => _openFolder(_favorite.Path));
        _menu.Items.Add("移除收藏", null, (_, _) => _removeFavorite(_favorite));
        _menu.Items.Add("复制路径", null, (_, _) => _copyPath(_favorite.Path));
        ContextMenuStrip = _menu;
        foreach (Control control in Controls)
        {
            control.ContextMenuStrip = _menu;
        }
    }

    private void WireDoubleClick(Control control)
    {
        control.Cursor = Cursors.Hand;
        control.DoubleClick += (_, _) =>
        {
            if (Directory.Exists(_favorite.Path))
            {
                _openFolder(_favorite.Path);
            }
        };
    }

    private static string CompactPath(string folderPath)
    {
        var trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        var parent = Path.GetDirectoryName(trimmed);
        var parentName = string.IsNullOrWhiteSpace(parent)
            ? string.Empty
            : Path.GetFileName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(parentName) ? folderPath : $"{parentName} \\ {name}";
    }

    private static string RelativeTime(DateTime time)
    {
        var span = DateTime.Now - time;
        if (span.TotalSeconds < 60)
        {
            return "刚刚";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{Math.Max(1, (int)span.TotalMinutes)} 分钟";
        }

        if (span.TotalHours < 24)
        {
            return $"{Math.Max(1, (int)span.TotalHours)} 小时";
        }

        return time.ToString("MM-dd");
    }
}
