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
    private const int HeaderHeight = 72;
    private const int FileRowHeight = 24;
    private const int MaxFileRows = 6;
    private readonly Action<FavoriteFile, FileCommand> _fileCommand;
    private readonly Label[] _fileLabels = new Label[MaxFileRows];
    private readonly Label _moreFilesLabel = new();
    private readonly ContextMenuStrip _fileMenu = new();
    private readonly Font _fileFont = new("Microsoft YaHei UI", 8.5F);
    private FavoriteFile? _fileMenuTarget;
    private FavoriteFolder _favorite;
    private bool _isLatest;

    public FavoriteFolderCard(
        FavoriteFolder favorite,
        bool isLatest,
        int width,
        Action<string> openFolder,
        Action<FavoriteFolder> removeFavorite,
        Action<string> copyPath,
        Action<FavoriteFile, FileCommand>? fileCommand = null)
    {
        _fileCommand = fileCommand ?? ((_, _) => { });
        _favorite = favorite;
        _openFolder = openFolder;
        _removeFavorite = removeFavorite;
        _copyPath = copyPath;

        Height = HeaderHeight;
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

        var cardColor = isLatest ? Theme.CardLatest : Theme.Card;
        BackColor = cardColor;
        _icon.BackColor = cardColor;
        _name.BackColor = cardColor;
        _path.BackColor = cardColor;
        _time.BackColor = cardColor;

        _name.Text = favorite.DisplayName;
        _path.Text = CompactPath(favorite.Path);
        RefreshRelativeTime();
        _name.ForeColor = Theme.Text;
        _path.ForeColor = Theme.Muted;
        _time.ForeColor = Theme.Muted;

        var textWidth = Math.Max(130, width - 182);
        _name.Width = textWidth;
        _path.Width = textWidth;
        _time.Location = new Point(Math.Max(238, width - 104), 20);

        UpdateFiles(favorite, cardColor, width);

        _toolTip.SetToolTip(this, favorite.Path);
        _toolTip.SetToolTip(_name, favorite.Path);
        _toolTip.SetToolTip(_path, favorite.Path);
        Invalidate();
    }

    public void RefreshRelativeTime()
    {
        _time.Text = RelativeTime(_favorite.LastActivity);
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
            _fileMenu.Dispose();
            _fileFont.Dispose();
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
        _time.Size = new Size(80, 30);
        Controls.Add(_time);

        for (var index = 0; index < _fileLabels.Length; index++)
        {
            var label = new Label
            {
                AutoEllipsis = true,
                Font = _fileFont,
                ForeColor = Theme.Text,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(72, HeaderHeight + index * FileRowHeight),
                Height = FileRowHeight - 2,
                Cursor = Cursors.Hand,
                Visible = false
            };
            label.MouseDown += (sender, e) =>
            {
                if (sender is Label { Tag: FavoriteFile file })
                {
                    _fileMenuTarget = file;
                }
            };
            label.Click += (sender, e) =>
            {
                if (e is MouseEventArgs { Button: MouseButtons.Left } && sender is Label { Tag: FavoriteFile file })
                {
                    _fileCommand(file, FileCommand.Open);
                }
            };
            _fileLabels[index] = label;
            Controls.Add(label);
        }

        _moreFilesLabel.AutoEllipsis = true;
        _moreFilesLabel.Font = _pathFont;
        _moreFilesLabel.ForeColor = Theme.Muted;
        _moreFilesLabel.TextAlign = ContentAlignment.MiddleLeft;
        _moreFilesLabel.Height = FileRowHeight - 2;
        _moreFilesLabel.Visible = false;
        Controls.Add(_moreFilesLabel);

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

        _fileMenu.ShowImageMargin = false;
        AddFileMenuItem("打开", FileCommand.Open);
        AddFileMenuItem("在文件夹中显示", FileCommand.Reveal);
        _fileMenu.Items.Add(new ToolStripSeparator());
        AddFileMenuItem("复制文件", FileCommand.CopyFile);
        AddFileMenuItem("复制文件路径", FileCommand.CopyPath);
        _fileMenu.Items.Add(new ToolStripSeparator());
        AddFileMenuItem("取消收藏", FileCommand.ToggleFavorite);
        foreach (var label in _fileLabels)
        {
            label.ContextMenuStrip = _fileMenu;
        }
    }

    private void AddFileMenuItem(string text, FileCommand command)
    {
        _fileMenu.Items.Add(text, null, (_, _) =>
        {
            if (_fileMenuTarget is not null)
            {
                _fileCommand(_fileMenuTarget, command);
            }
        });
    }

    private void UpdateFiles(FavoriteFolder favorite, Color cardColor, int width)
    {
        var files = favorite.Files;
        var shown = Math.Min(files.Count, MaxFileRows);
        for (var index = 0; index < _fileLabels.Length; index++)
        {
            var label = _fileLabels[index];
            if (index >= shown)
            {
                label.Visible = false;
                label.Tag = null;
                continue;
            }

            var file = files[index];
            var exists = File.Exists(file.Path);
            label.Tag = file;
            label.Text = $"★ {file.RelativeName}";
            label.ForeColor = exists ? Theme.Text : Theme.Dim;
            label.BackColor = cardColor;
            label.Width = Math.Max(120, width - 90);
            label.Visible = true;
            _toolTip.SetToolTip(label, exists ? file.Path : $"{file.Path}\r\n文件已不存在或已被移动");
        }

        var hidden = files.Count - shown;
        _moreFilesLabel.Visible = hidden > 0;
        _moreFilesLabel.Text = hidden > 0 ? $"还有 {hidden} 个收藏文件" : string.Empty;
        _moreFilesLabel.BackColor = cardColor;
        _moreFilesLabel.Location = new Point(72, HeaderHeight + shown * FileRowHeight);
        _moreFilesLabel.Width = Math.Max(120, width - 90);

        var rows = shown + (hidden > 0 ? 1 : 0);
        Height = rows == 0 ? HeaderHeight : HeaderHeight + rows * FileRowHeight + 6;
    }

    private void WireDoubleClick(Control control)
    {
        control.Cursor = Cursors.Hand;
        control.DoubleClick += (_, _) =>
        {
            _openFolder(_favorite.Path);
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

    private static string RelativeTime(DateTime time) => TimeText.Relative(time);
}
