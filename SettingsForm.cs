namespace DiskWriteWatcher;

public sealed class SettingsForm : Form
{
    private readonly CheckedListBox _driveList = new();
    private readonly ListBox _excludeList = new();
    private readonly NumericUpDown _floatingFavoriteCount = new();
    private readonly List<WatchScope> _watchScopes;
    private readonly List<string> _excludedPaths;

    public SettingsForm(
        IEnumerable<WatchScope> watchScopes,
        IEnumerable<string> excludedPaths,
        int floatingFavoriteCount)
    {
        _watchScopes = watchScopes.Select(scope => new WatchScope(scope.Path, scope.Enabled)).ToList();
        _excludedPaths = excludedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _floatingFavoriteCount.Value = AppSettings.NormalizeFloatingFavoriteCount(floatingFavoriteCount);

        Text = "监视设置";
        Size = new Size(460, 560);
        MinimumSize = new Size(420, 500);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildUi();
        _driveList.ItemCheck += PreventUnsupportedDriveSelection;
        LoadDrives();
        LoadExclusions();
    }

    public IReadOnlyList<WatchScope> WatchScopes => _watchScopes;
    public IReadOnlyList<string> ExcludedPaths => _excludedPaths;
    public int FloatingFavoriteCount => (int)_floatingFavoriteCount.Value;

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            Padding = new Padding(12),
            BackColor = Theme.Window
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "监视硬盘和排除文件夹",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        root.Controls.Add(tabs, 0, 1);

        var drivePage = new TabPage("监视硬盘")
        {
            BackColor = Theme.Window,
            ForeColor = Theme.Text,
            Padding = new Padding(8)
        };
        tabs.TabPages.Add(drivePage);
        BuildDrivePage(drivePage);

        var excludePage = new TabPage("排除文件夹")
        {
            BackColor = Theme.Window,
            ForeColor = Theme.Text,
            Padding = new Padding(8)
        };
        tabs.TabPages.Add(excludePage);
        BuildExcludePage(excludePage);

        var floatingPage = new TabPage("浮窗")
        {
            BackColor = Theme.Window,
            ForeColor = Theme.Text,
            Padding = new Padding(8)
        };
        tabs.TabPages.Add(floatingPage);
        BuildFloatingPage(floatingPage);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Window
        };
        root.Controls.Add(footer, 0, 2);

        var okButton = CreateButton("确定", primary: true);
        okButton.DialogResult = DialogResult.OK;
        okButton.Click += (_, _) => Save();
        footer.Controls.Add(okButton);

        var cancelButton = CreateButton("取消", primary: false);
        cancelButton.DialogResult = DialogResult.Cancel;
        footer.Controls.Add(cancelButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private void BuildDrivePage(Control page)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Theme.Window
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        page.Controls.Add(root);

        _driveList.Dock = DockStyle.Fill;
        _driveList.CheckOnClick = true;
        _driveList.BackColor = Theme.Panel;
        _driveList.ForeColor = Theme.Text;
        _driveList.BorderStyle = BorderStyle.FixedSingle;
        root.Controls.Add(_driveList, 0, 0);

        root.Controls.Add(new Label
        {
            Text = "勾选整盘后，右侧主窗口会显示最近活跃文件夹。",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
    }

    private void BuildExcludePage(Control page)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Theme.Window
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        page.Controls.Add(root);

        _excludeList.Dock = DockStyle.Fill;
        _excludeList.BackColor = Theme.Panel;
        _excludeList.ForeColor = Theme.Text;
        _excludeList.BorderStyle = BorderStyle.FixedSingle;
        root.Controls.Add(_excludeList, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Window
        };
        root.Controls.Add(buttons, 0, 1);

        var addButton = CreateButton("添加", primary: false);
        addButton.Click += (_, _) => AddExcludeFolder();
        buttons.Controls.Add(addButton);

        var removeButton = CreateButton("移除", primary: false);
        removeButton.Click += (_, _) => RemoveSelectedExclusion();
        buttons.Controls.Add(removeButton);
    }

    private void BuildFloatingPage(Control page)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 92,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Theme.Window,
            Padding = new Padding(4, 8, 4, 0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        page.Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "收藏显示数量",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _floatingFavoriteCount.Dock = DockStyle.Fill;
        _floatingFavoriteCount.Minimum = AppSettings.MinFloatingFavoriteCount;
        _floatingFavoriteCount.Maximum = AppSettings.MaxFloatingFavoriteCount;
        _floatingFavoriteCount.BackColor = Theme.Panel;
        _floatingFavoriteCount.ForeColor = Theme.Text;
        _floatingFavoriteCount.BorderStyle = BorderStyle.FixedSingle;
        _floatingFavoriteCount.TextAlign = HorizontalAlignment.Center;
        root.Controls.Add(_floatingFavoriteCount, 1, 0);

        var description = new Label
        {
            Text = "包含底部固定的最新收藏，展开栏始终从下往上排列。",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        root.SetColumnSpan(description, 2);
        root.Controls.Add(description, 0, 1);
    }

    private static Button CreateButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Width = 78,
            Height = 28,
            Margin = new Padding(6, 6, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Theme.AccentDark : Theme.Panel,
            ForeColor = primary ? Color.White : Theme.Text,
            TabStop = false
        };
        button.FlatAppearance.BorderColor = primary ? Theme.AccentDark : Theme.Border;
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private void LoadDrives()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
            {
                continue;
            }

            var existing = _watchScopes.FirstOrDefault(scope =>
                string.Equals(scope.Path, drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase));
            var supported = drive.DriveFormat is "NTFS" or "ReFS";
            if (existing is null)
            {
                existing = new WatchScope(drive.RootDirectory.FullName);
                existing.Enabled = false;
                _watchScopes.Add(existing);
            }

            if (!supported)
            {
                existing.Enabled = false;
            }

            var index = _driveList.Items.Add(new DriveItem(existing, drive.VolumeLabel, drive.DriveType, drive.DriveFormat, supported));
            _driveList.SetItemChecked(index, existing.Enabled);
        }
    }

    private void PreventUnsupportedDriveSelection(object? sender, ItemCheckEventArgs e)
    {
        if (e.Index >= 0
            && e.Index < _driveList.Items.Count
            && _driveList.Items[e.Index] is DriveItem { Supported: false })
        {
            e.NewValue = CheckState.Unchecked;
        }
    }

    private void LoadExclusions()
    {
        foreach (var path in _excludedPaths)
        {
            _excludeList.Items.Add(path);
        }
    }

    private void AddExcludeFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择要排除的文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (_excludeList.Items.Cast<object>().Any(item =>
            string.Equals(item.ToString(), dialog.SelectedPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _excludeList.Items.Add(dialog.SelectedPath);
    }

    private void RemoveSelectedExclusion()
    {
        var selected = _excludeList.SelectedItems.Cast<object>().ToArray();
        foreach (var item in selected)
        {
            _excludeList.Items.Remove(item);
        }
    }

    private void Save()
    {
        foreach (var item in _driveList.Items.Cast<DriveItem>())
        {
            item.Scope.Enabled = _driveList.CheckedItems.Contains(item);
        }

        _excludedPaths.Clear();
        foreach (var item in _excludeList.Items.Cast<object>())
        {
            var path = item.ToString();
            if (!string.IsNullOrWhiteSpace(path)
                && !_excludedPaths.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
            {
                _excludedPaths.Add(path);
            }
        }
    }

    private sealed record DriveItem(
        WatchScope Scope,
        string VolumeLabel,
        DriveType DriveType,
        string FileSystem,
        bool Supported)
    {
        public override string ToString()
        {
            var label = string.IsNullOrWhiteSpace(VolumeLabel) ? "无卷标" : VolumeLabel;
            var support = Supported ? string.Empty : "  不支持 USN";
            return $"{Scope.Path}  {label}  {FileSystem}  ({DriveType}){support}";
        }
    }
}
