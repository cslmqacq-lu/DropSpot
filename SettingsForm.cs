namespace DropSpot;

public sealed class SettingsForm : Form
{
    private readonly CheckedListBox _driveList = new();
    private readonly ListBox _excludeList = new();
    private readonly NumericUpDown _floatingFavoriteCount = new();
    private readonly NumericUpDown _floatingOpacity = new();
    private readonly Button _floatingColorButton = new();
    private readonly CheckBox _startWithWindows = new();
    private readonly List<WatchScope> _watchScopes;
    private readonly List<string> _excludedPaths;
    private Color _floatingBackgroundColor;

    public SettingsForm(
        IEnumerable<WatchScope> watchScopes,
        IEnumerable<string> excludedPaths,
        int floatingFavoriteCount,
        int floatingBackgroundArgb,
        int floatingOpacityPercent,
        bool startWithWindows)
    {
        _watchScopes = watchScopes.Select(scope => new WatchScope(scope.Path, scope.Enabled)).ToList();
        _excludedPaths = excludedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _floatingFavoriteCount.Value = AppSettings.NormalizeFloatingFavoriteCount(floatingFavoriteCount);
        _floatingBackgroundColor = AppSettings.GetFloatingBackgroundColor(floatingBackgroundArgb);
        _floatingOpacity.Value = AppSettings.NormalizeFloatingOpacityPercent(floatingOpacityPercent);
        _startWithWindows.Checked = startWithWindows;

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
    public int FloatingBackgroundArgb => _floatingBackgroundColor.ToArgb();
    public int FloatingOpacityPercent => (int)_floatingOpacity.Value;
    public bool StartWithWindows => _startWithWindows.Checked;

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
            Text = "监视与外观设置",
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

        var generalPage = new TabPage("常规")
        {
            BackColor = Theme.Window,
            ForeColor = Theme.Text,
            Padding = new Padding(8)
        };
        tabs.TabPages.Add(generalPage);
        BuildGeneralPage(generalPage);

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

        var aboutPage = new TabPage("关于")
        {
            BackColor = Theme.Window,
            ForeColor = Theme.Text,
            Padding = new Padding(8)
        };
        tabs.TabPages.Add(aboutPage);
        BuildAboutPage(aboutPage);

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

    private void BuildGeneralPage(Control page)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 82,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(8, 12, 8, 0),
            BackColor = Theme.Window
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        page.Controls.Add(root);

        _startWithWindows.Text = "随 Windows 启动";
        _startWithWindows.Dock = DockStyle.Fill;
        _startWithWindows.ForeColor = Theme.Text;
        _startWithWindows.BackColor = Theme.Window;
        _startWithWindows.AutoSize = false;
        root.Controls.Add(_startWithWindows, 0, 0);

        root.Controls.Add(new Label
        {
            Text = "登录后自动开始监视，并进入浮窗。",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
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
            Height = 206,
            ColumnCount = 2,
            RowCount = 5,
            BackColor = Theme.Window,
            Padding = new Padding(4, 8, 4, 0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
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

        root.Controls.Add(new Label
        {
            Text = "背景色",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);

        _floatingColorButton.Dock = DockStyle.Fill;
        _floatingColorButton.Margin = new Padding(0, 5, 0, 5);
        _floatingColorButton.FlatStyle = FlatStyle.Flat;
        _floatingColorButton.Click += (_, _) => ChooseFloatingColor();
        root.Controls.Add(_floatingColorButton, 1, 1);
        UpdateFloatingColorButton();

        root.Controls.Add(new Label
        {
            Text = "背景不透明度",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 2);

        _floatingOpacity.Dock = DockStyle.Fill;
        _floatingOpacity.Minimum = AppSettings.MinFloatingOpacityPercent;
        _floatingOpacity.Maximum = AppSettings.MaxFloatingOpacityPercent;
        _floatingOpacity.Increment = 5;
        _floatingOpacity.BackColor = Theme.Panel;
        _floatingOpacity.ForeColor = Theme.Text;
        _floatingOpacity.BorderStyle = BorderStyle.FixedSingle;
        _floatingOpacity.TextAlign = HorizontalAlignment.Center;
        root.Controls.Add(_floatingOpacity, 1, 2);

        root.Controls.Add(new Label
        {
            Text = "恢复默认外观",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 3);

        var resetButton = CreateButton("默认", primary: false);
        resetButton.Dock = DockStyle.Fill;
        resetButton.Margin = new Padding(0, 5, 0, 5);
        resetButton.Click += (_, _) => RestoreDefaultFloatingAppearance();
        root.Controls.Add(resetButton, 1, 3);

        var description = new Label
        {
            Text = "收藏从下往上排列；颜色和不透明度统一用于浮窗、展开栏和信息框。",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        root.SetColumnSpan(description, 2);
        root.Controls.Add(description, 0, 4);
    }

    private static void BuildAboutPage(Control page)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 150,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Window,
            Padding = new Padding(8, 22, 8, 0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        page.Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "DropSpot",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Text = $"版本：{Application.ProductVersion}",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleCenter
        }, 0, 1);
        root.Controls.Add(new Label
        {
            Text = "开发者：cslm",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleCenter
        }, 0, 2);
    }

    private void ChooseFloatingColor()
    {
        using var dialog = new ColorDialog
        {
            Color = _floatingBackgroundColor,
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _floatingBackgroundColor = AppSettings.GetFloatingBackgroundColor(dialog.Color.ToArgb());
            UpdateFloatingColorButton();
        }
    }

    private void RestoreDefaultFloatingAppearance()
    {
        _floatingBackgroundColor = AppSettings.GetFloatingBackgroundColor(AppSettings.DefaultFloatingBackgroundArgb);
        _floatingOpacity.Value = AppSettings.DefaultFloatingOpacityPercent;
        UpdateFloatingColorButton();
    }

    private void UpdateFloatingColorButton()
    {
        _floatingColorButton.Text = $"#{_floatingBackgroundColor.R:X2}{_floatingBackgroundColor.G:X2}{_floatingBackgroundColor.B:X2}";
        _floatingColorButton.BackColor = _floatingBackgroundColor;
        _floatingColorButton.ForeColor = Theme.TextForBackground(_floatingBackgroundColor);
        _floatingColorButton.FlatAppearance.BorderColor = Theme.BorderForBackground(_floatingBackgroundColor);
        _floatingColorButton.FlatAppearance.BorderSize = 1;
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
