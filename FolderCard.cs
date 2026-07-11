namespace DropSpot;

public sealed class FolderCard : UserControl
{
    private readonly Action<string> _openFolder;
    private readonly Action<FolderActivity> _toggleExpanded;
    private readonly Action<ChangeRecord> _openFile;
    private readonly Action<FolderActivity> _excludeFolder;
    private readonly Action<FolderActivity> _copyFolderPath;
    private readonly Action<FolderActivity> _addFavorite;
    private readonly Func<string, bool> _isFavorite;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _favoriteItem = new();
    private readonly ToolTip _toolTip = new();
    private readonly Panel _headerRow = new();
    private readonly Panel _filePanel = new();
    private readonly PictureBox _folderIcon = new();
    private readonly Label _nameLabel = new();
    private readonly Label _pathLabel = new();
    private readonly Label _metaLabel = new();
    private readonly Button _expandButton = new();
    private readonly FileRow[] _fileRows = new FileRow[3];
    private readonly Font _titleFont = new("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
    private readonly Font _pathFont = new("Microsoft YaHei UI", 8F);
    private readonly Font _metaFont = new("Microsoft YaHei UI", 8F);
    private readonly Font _fileFont = new("Microsoft YaHei UI", 8.5F);
    private readonly Font _fileTimeFont = new("Microsoft YaHei UI", 8F);
    private readonly Font _expandFont = new("Segoe MDL2 Assets", 10F);
    private FolderActivity _folder;
    private bool _isLatest;

    public FolderCard(
        FolderActivity folder,
        bool expanded,
        bool isLatest,
        int width,
        Action<string> openFolder,
        Action<FolderActivity> toggleExpanded,
        Action<ChangeRecord> openFile,
        Action<FolderActivity> excludeFolder,
        Action<FolderActivity> copyFolderPath,
        Action<FolderActivity> addFavorite,
        Func<string, bool> isFavorite)
    {
        _folder = folder;
        _isLatest = isLatest;
        _openFolder = openFolder;
        _toggleExpanded = toggleExpanded;
        _openFile = openFile;
        _excludeFolder = excludeFolder;
        _copyFolderPath = copyFolderPath;
        _addFavorite = addFavorite;
        _isFavorite = isFavorite;

        Margin = new Padding(0, 0, 0, 8);
        BackColor = Theme.Card;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;

        BuildMenu();
        BuildHeader();
        BuildFileRows();
        UpdateActivity(folder, expanded, isLatest, width);
    }

    public string FolderPath => _folder.FolderPath;
    internal Rectangle FilePanelBounds => _filePanel.Bounds;
    internal int VisibleFileRowCount => _fileRows.Count(row => row.HasRecord);

    public void UpdateActivity(FolderActivity folder, bool expanded, bool isLatest, int width)
    {
        _folder = folder;
        _isLatest = isLatest;
        Width = width;
        Height = expanded ? 104 + Math.Min(3, folder.Files.Count) * 30 : 88;

        _nameLabel.Text = folder.DisplayName;
        _pathLabel.Text = CompactPath(folder.FolderPath);
        _expandButton.Text = expanded ? "\uE70E" : "\uE70D";
        _filePanel.Visible = expanded;
        _toolTip.SetToolTip(this, folder.FolderPath);
        _toolTip.SetToolTip(_nameLabel, folder.FolderPath);
        _toolTip.SetToolTip(_pathLabel, folder.FolderPath);

        for (var index = 0; index < _fileRows.Length; index++)
        {
            var record = folder.Files.ElementAtOrDefault(index);
            _fileRows[index].Update(record, width);
        }

        UpdateLayout(width);
        ApplyLatestStyle();
        RefreshRelativeTime();
    }

    public void RefreshRelativeTime()
    {
        _metaLabel.Text = $"{RelativeTime(_folder.LastTime)}\r\n{_folder.ChangeCount} 次";
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
            _titleFont.Dispose();
            _pathFont.Dispose();
            _metaFont.Dispose();
            _fileFont.Dispose();
            _fileTimeFont.Dispose();
            _expandFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildMenu()
    {
        _menu.Items.Add("打开文件夹", null, (_, _) => _openFolder(_folder.FolderPath));
        _favoriteItem.Click += (_, _) => _addFavorite(_folder);
        _menu.Items.Add(_favoriteItem);
        _menu.Items.Add("加入排除", null, (_, _) => _excludeFolder(_folder));
        _menu.Items.Add("复制路径", null, (_, _) => _copyFolderPath(_folder));
        _menu.Opening += (_, _) =>
        {
            var favorite = _isFavorite(_folder.FolderPath);
            _favoriteItem.Text = favorite ? "已收藏" : "加入收藏";
            _favoriteItem.Enabled = !favorite;
        };
        ContextMenuStrip = _menu;
    }

    private void BuildHeader()
    {
        _headerRow.Dock = DockStyle.Top;
        _headerRow.Height = 88;
        _headerRow.BackColor = Theme.Card;
        _headerRow.Cursor = Cursors.Hand;
        _headerRow.DoubleClick += (_, _) => _openFolder(_folder.FolderPath);

        _folderIcon.Image = ShellIconProvider.FolderIcon();
        _folderIcon.BackColor = Theme.Card;
        _folderIcon.SizeMode = PictureBoxSizeMode.CenterImage;
        _folderIcon.Location = new Point(14, 20);
        _folderIcon.Size = new Size(48, 48);
        _folderIcon.Cursor = Cursors.Hand;
        _folderIcon.DoubleClick += (_, _) => _openFolder(_folder.FolderPath);
        _headerRow.Controls.Add(_folderIcon);

        ConfigureHeaderLabel(_nameLabel, _titleFont, Theme.Text, new Point(72, 17), 24);
        _nameLabel.DoubleClick += (_, _) => _openFolder(_folder.FolderPath);
        _headerRow.Controls.Add(_nameLabel);

        ConfigureHeaderLabel(_pathLabel, _pathFont, Theme.Muted, new Point(72, 46), 21);
        _pathLabel.DoubleClick += (_, _) => _openFolder(_folder.FolderPath);
        _headerRow.Controls.Add(_pathLabel);

        _metaLabel.ForeColor = Theme.Muted;
        _metaLabel.BackColor = Theme.Card;
        _metaLabel.Font = _metaFont;
        _metaLabel.TextAlign = ContentAlignment.MiddleRight;
        _metaLabel.Size = new Size(60, 42);
        _headerRow.Controls.Add(_metaLabel);

        _expandButton.FlatStyle = FlatStyle.Flat;
        _expandButton.BackColor = Theme.CardAlt;
        _expandButton.ForeColor = Theme.Text;
        _expandButton.Font = _expandFont;
        _expandButton.Size = new Size(30, 30);
        _expandButton.TabStop = false;
        _expandButton.FlatAppearance.BorderColor = Theme.BorderStrong;
        _expandButton.FlatAppearance.BorderSize = 1;
        _expandButton.Click += (_, _) => _toggleExpanded(_folder);
        _headerRow.Controls.Add(_expandButton);

        Controls.Add(_headerRow);
    }

    private void BuildFileRows()
    {
        _filePanel.Dock = DockStyle.None;
        _filePanel.BackColor = Theme.CardAlt;
        _filePanel.Padding = new Padding(72, 5, 10, 8);
        Controls.Add(_filePanel);
        _filePanel.BringToFront();
        _headerRow.BringToFront();

        for (var index = 0; index < _fileRows.Length; index++)
        {
            var row = new FileRow(_openFile, _fileFont, _fileTimeFont)
            {
                Top = 6 + index * 30
            };
            _fileRows[index] = row;
            _filePanel.Controls.Add(row.Panel);
        }
    }

    private static void ConfigureHeaderLabel(Label label, Font font, Color color, Point location, int height)
    {
        label.AutoEllipsis = true;
        label.ForeColor = color;
        label.BackColor = Theme.Card;
        label.Font = font;
        label.Location = location;
        label.Height = height;
        label.Cursor = Cursors.Hand;
    }

    private void UpdateLayout(int width)
    {
        _filePanel.SetBounds(0, _headerRow.Height, width, Math.Max(0, Height - _headerRow.Height));
        var textWidth = Math.Max(120, width - 198);
        _nameLabel.Width = textWidth;
        _pathLabel.Width = textWidth;
        _metaLabel.Location = new Point(Math.Max(240, width - 112), 20);
        _expandButton.Location = new Point(Math.Max(326, width - 46), 28);

        foreach (var row in _fileRows)
        {
            row.UpdateWidth(width);
        }
    }

    private void ApplyLatestStyle()
    {
        var cardColor = _isLatest ? Theme.CardLatest : Theme.Card;
        BackColor = cardColor;
        _headerRow.BackColor = cardColor;
        _folderIcon.BackColor = cardColor;
        _nameLabel.BackColor = cardColor;
        _pathLabel.BackColor = cardColor;
        _metaLabel.BackColor = cardColor;
        Invalidate();
    }

    private static string CompactPath(string folderPath)
    {
        var trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        var parent = Path.GetDirectoryName(trimmed);
        var parentName = string.IsNullOrWhiteSpace(parent)
            ? string.Empty
            : Path.GetFileName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(name))
        {
            return folderPath;
        }

        return string.IsNullOrWhiteSpace(parentName) ? name : $"{parentName} \\ {name}";
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

        return time.ToString("HH:mm");
    }

    private sealed class FileRow
    {
        private readonly Action<ChangeRecord> _openFile;
        private readonly PictureBox _icon = new();
        private readonly Label _name = new();
        private readonly Label _time = new();
        private ChangeRecord? _record;

        public FileRow(Action<ChangeRecord> openFile, Font fileFont, Font timeFont)
        {
            _openFile = openFile;
            Panel = new Panel
            {
                Left = 72,
                Height = 25,
                BackColor = Theme.CardAlt,
                Cursor = Cursors.Hand,
                Visible = false
            };
            Panel.Click += OpenCurrent;

            _icon.BackColor = Theme.CardAlt;
            _icon.SizeMode = PictureBoxSizeMode.CenterImage;
            _icon.Location = new Point(0, 4);
            _icon.Size = new Size(18, 18);
            _icon.Cursor = Cursors.Hand;
            _icon.Click += OpenCurrent;
            Panel.Controls.Add(_icon);

            _name.AutoEllipsis = true;
            _name.ForeColor = Theme.Text;
            _name.BackColor = Theme.CardAlt;
            _name.Font = fileFont;
            _name.Location = new Point(26, 2);
            _name.Height = 21;
            _name.Cursor = Cursors.Hand;
            _name.Click += OpenCurrent;
            Panel.Controls.Add(_name);

            _time.ForeColor = Theme.Muted;
            _time.BackColor = Theme.CardAlt;
            _time.Font = timeFont;
            _time.TextAlign = ContentAlignment.MiddleRight;
            _time.Height = 21;
            _time.Click += OpenCurrent;
            Panel.Controls.Add(_time);
        }

        public Panel Panel { get; }
        public bool HasRecord => _record is not null;

        public int Top
        {
            set => Panel.Top = value;
        }

        public void Update(ChangeRecord? record, int cardWidth)
        {
            _record = record;
            Panel.Visible = record is not null;
            if (record is not null)
            {
                _icon.Image = ShellIconProvider.FileIcon(record.FilePath);
                _name.Text = record.FileName;
                _time.Text = record.Time.ToString("HH:mm");
            }

            UpdateWidth(cardWidth);
        }

        public void UpdateWidth(int cardWidth)
        {
            Panel.Width = Math.Max(180, cardWidth - 90);
            _name.Width = Math.Max(90, cardWidth - 178);
            _time.Location = new Point(Math.Max(130, cardWidth - 146), 2);
            _time.Width = 46;
        }

        private void OpenCurrent(object? sender, EventArgs e)
        {
            if (_record is not null)
            {
                _openFile(_record);
            }
        }
    }
}
