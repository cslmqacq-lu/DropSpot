namespace DropSpot;

/// <summary>文件行（右键菜单）可执行的操作。</summary>
public enum FileCommand
{
    Open,
    Reveal,
    OpenWith,
    CopyFile,
    CopyPath,
    CopyName,
    /// <summary>收藏 / 取消收藏这个文件。</summary>
    ToggleFavorite
}

public sealed class FolderCard : UserControl
{
    private readonly Action<string> _openFolder;
    private readonly Action<FolderActivity> _toggleExpanded;
    private readonly Action<ChangeRecord> _openFile;
    private readonly Action<FolderActivity> _excludeFolder;
    private readonly Action<FolderActivity> _copyFolderPath;
    private readonly Action<FolderActivity> _addFavorite;
    private readonly Action<FolderActivity> _pinFolder;
    private readonly Func<string, bool> _isFavorite;
    private readonly Func<string, bool> _isPinned;
    private readonly Action<ChangeRecord, FileCommand> _fileCommand;
    private readonly Func<string, bool> _isFileFavorite;
    private readonly ToolStripMenuItem _fileFavoriteItem = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _favoriteItem = new();
    private readonly ToolStripMenuItem _pinItem = new();
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
    private readonly ContextMenuStrip _fileMenu = new();
    private ChangeRecord? _fileMenuTarget;
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
        Func<string, bool> isFavorite,
        Action<FolderActivity> pinFolder,
        Func<string, bool> isPinned,
        Action<ChangeRecord, FileCommand>? fileCommand = null,
        Func<string, bool>? isFileFavorite = null)
    {
        _isFileFavorite = isFileFavorite ?? (_ => false);
        _folder = folder;
        _isLatest = isLatest;
        _openFolder = openFolder;
        _toggleExpanded = toggleExpanded;
        _openFile = openFile;
        _excludeFolder = excludeFolder;
        _copyFolderPath = copyFolderPath;
        _addFavorite = addFavorite;
        _isFavorite = isFavorite;
        _pinFolder = pinFolder;
        _isPinned = isPinned;
        _fileCommand = fileCommand ?? ((record, command) =>
        {
            if (command == FileCommand.Open)
            {
                openFile(record);
            }
        });

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
        // 最近文件只显示没被过滤的（临时文件、开发产物、自定义隐藏扩展名都不显示）
        var visibleFiles = folder.Files.Where(file => !PathRules.IsHiddenFromRecent(file.FilePath)).Take(_fileRows.Length).ToArray();
        Height = expanded ? 104 + visibleFiles.Length * 30 : 88;

        _nameLabel.Text = folder.DisplayName;
        _pathLabel.Text = CompactPath(folder.FolderPath);
        _expandButton.Text = expanded ? "\uE70E" : "\uE70D";
        _filePanel.Visible = expanded;
        _toolTip.SetToolTip(this, folder.FolderPath);
        _toolTip.SetToolTip(_nameLabel, folder.FolderPath);
        _toolTip.SetToolTip(_pathLabel, folder.FolderPath);

        for (var index = 0; index < _fileRows.Length; index++)
        {
            var record = visibleFiles.ElementAtOrDefault(index);
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
            _fileMenu.Dispose();
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
        _pinItem.Click += (_, _) => _pinFolder(_folder);
        _menu.Items.Add(_pinItem);
        _menu.Items.Add("加入排除", null, (_, _) => _excludeFolder(_folder));
        _menu.Items.Add("复制路径", null, (_, _) => _copyFolderPath(_folder));
        _menu.Opening += (_, _) =>
        {
            var favorite = _isFavorite(_folder.FolderPath);
            _favoriteItem.Text = favorite ? "已收藏" : "加入收藏";
            _favoriteItem.Enabled = !favorite;
            var pinned = _isPinned(_folder.FolderPath);
            _pinItem.Text = pinned ? "已钉到浮窗" : "钉到浮窗";
            _pinItem.Enabled = !pinned;
        };
        ContextMenuStrip = _menu;

        _fileMenu.ShowImageMargin = false;
        AddFileMenuItem("打开", FileCommand.Open);
        AddFileMenuItem("在文件夹中显示", FileCommand.Reveal);
        AddFileMenuItem("打开方式…", FileCommand.OpenWith);
        _fileMenu.Items.Add(new ToolStripSeparator());
        AddFileMenuItem("复制文件", FileCommand.CopyFile);
        AddFileMenuItem("复制文件路径", FileCommand.CopyPath);
        AddFileMenuItem("复制文件名", FileCommand.CopyName);
        _fileMenu.Items.Add(new ToolStripSeparator());
        _fileFavoriteItem.Click += (_, _) =>
        {
            if (_fileMenuTarget is not null)
            {
                _fileCommand(_fileMenuTarget, FileCommand.ToggleFavorite);
            }
        };
        _fileMenu.Items.Add(_fileFavoriteItem);
        _fileMenu.Opening += (_, _) =>
        {
            var favorite = _fileMenuTarget is not null && _isFileFavorite(_fileMenuTarget.FilePath);
            _fileFavoriteItem.Text = favorite ? "取消收藏此文件" : "★ 收藏此文件";
        };
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

    private void SetFileMenuTarget(ChangeRecord record)
    {
        _fileMenuTarget = record;
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
        _metaLabel.Size = new Size(76, 42);
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
            var row = new FileRow(_openFile, SetFileMenuTarget, _fileMenu, path => _isFileFavorite(path), _fileFont, _fileTimeFont)
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
        var textWidth = Math.Max(120, width - 214);
        _nameLabel.Width = textWidth;
        _pathLabel.Width = textWidth;
        _metaLabel.Location = new Point(Math.Max(224, width - 128), 20);
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

    private static string RelativeTime(DateTime time) => TimeText.Relative(time);

    private sealed class FileRow
    {
        private readonly Action<ChangeRecord> _openFile;
        private readonly Action<ChangeRecord> _setMenuTarget;
        private readonly Func<string, bool> _isFavorite;
        private readonly PictureBox _icon = new();
        private readonly Label _name = new();
        private readonly Label _time = new();
        private ChangeRecord? _record;
        private string? _iconPath;
        private Point _mouseDownPosition;
        private bool _dragCandidate;

        public FileRow(
            Action<ChangeRecord> openFile,
            Action<ChangeRecord> setMenuTarget,
            ContextMenuStrip fileMenu,
            Func<string, bool> isFavorite,
            Font fileFont,
            Font timeFont)
        {
            _openFile = openFile;
            _setMenuTarget = setMenuTarget;
            _isFavorite = isFavorite;
            Panel = new Panel
            {
                Left = 72,
                Height = 25,
                BackColor = Theme.CardAlt,
                Cursor = Cursors.Hand,
                Visible = false
            };

            _icon.BackColor = Theme.CardAlt;
            _icon.SizeMode = PictureBoxSizeMode.CenterImage;
            _icon.Location = new Point(0, 4);
            _icon.Size = new Size(18, 18);
            _icon.Cursor = Cursors.Hand;
            Panel.Controls.Add(_icon);

            _name.AutoEllipsis = true;
            _name.ForeColor = Theme.Text;
            _name.BackColor = Theme.CardAlt;
            _name.Font = fileFont;
            _name.Location = new Point(26, 2);
            _name.Height = 21;
            _name.Cursor = Cursors.Hand;
            Panel.Controls.Add(_name);

            _time.ForeColor = Theme.Muted;
            _time.BackColor = Theme.CardAlt;
            _time.Font = timeFont;
            _time.TextAlign = ContentAlignment.MiddleRight;
            _time.Height = 21;
            Panel.Controls.Add(_time);

            // 单击打开；按住拖动时以标准 FileDrop 拖出真实文件；右键弹出文件菜单。
            foreach (var control in new Control[] { Panel, _icon, _name, _time })
            {
                control.ContextMenuStrip = fileMenu;
                control.MouseDown += HandleMouseDown;
                control.MouseMove += HandleMouseMove;
                control.MouseUp += HandleMouseUp;
            }

            Panel.MouseEnter += (_, _) => SetHover(true);
            Panel.MouseLeave += (_, _) =>
            {
                if (!Panel.ClientRectangle.Contains(Panel.PointToClient(Cursor.Position)))
                {
                    SetHover(false);
                }
            };
            foreach (var child in new Control[] { _icon, _name, _time })
            {
                child.MouseEnter += (_, _) => SetHover(true);
            }
        }

        private void SetHover(bool hover)
        {
            var color = hover ? Theme.Card : Theme.CardAlt;
            Panel.BackColor = color;
            _icon.BackColor = color;
            _name.BackColor = color;
            _time.BackColor = color;
        }

        private void HandleMouseDown(object? sender, MouseEventArgs e)
        {
            if (_record is null)
            {
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                _setMenuTarget(_record);
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
            if (!_dragCandidate || _record is null)
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
            ShellFileDrop.DoInternalFileDrag(Panel, _record.FilePath);
        }

        private void HandleMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            var shouldOpen = _dragCandidate;
            _dragCandidate = false;
            if (shouldOpen && _record is not null)
            {
                _openFile(_record);
            }
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
                if (!string.Equals(_iconPath, record.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    _icon.Image = ShellIconProvider.FileIcon(record.FilePath);
                    _iconPath = record.FilePath;
                }
                _name.Text = _isFavorite(record.FilePath) ? $"★ {record.FileName}" : record.FileName;
                _time.Text = TimeText.Clock(record.Time);
            }
            else
            {
                _icon.Image = null;
                _iconPath = null;
            }

            UpdateWidth(cardWidth);
        }

        public void UpdateWidth(int cardWidth)
        {
            Panel.Width = Math.Max(180, cardWidth - 90);
            _name.Width = Math.Max(70, cardWidth - 208);
            _time.Location = new Point(Math.Max(100, cardWidth - 176), 2);
            _time.Width = 76;
        }
    }
}
