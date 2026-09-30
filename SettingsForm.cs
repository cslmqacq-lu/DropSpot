namespace DropSpot;

public sealed class SettingsForm : Form
{
    private readonly CheckedListBox _driveList = new();
    private readonly ListBox _excludeList = new();
    private readonly NumericUpDown _floatingFavoriteCount = new();
    private readonly CheckBox _startWithWindows = new();
    private readonly HotKeyEditor _openLatestHotKey = new();
    private readonly HotKeyEditor _copyLatestPathHotKey = new();
    private readonly Label _hotKeyError = new();
    private readonly List<WatchScope> _watchScopes;
    private readonly List<string> _excludedPaths;
    private readonly Action? _openDiagnostics;

    public SettingsForm(
        IEnumerable<WatchScope> watchScopes,
        IEnumerable<string> excludedPaths,
        int floatingFavoriteCount,
        bool startWithWindows,
        Action? openDiagnostics = null,
        SavedHotKey? openLatestFolderHotKey = null,
        SavedHotKey? copyLatestFolderPathHotKey = null)
    {
        _watchScopes = watchScopes.Select(scope => new WatchScope(scope.Path, scope.Enabled)).ToList();
        _excludedPaths = excludedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _floatingFavoriteCount.Value = AppSettings.NormalizeFloatingFavoriteCount(floatingFavoriteCount);
        _startWithWindows.Checked = startWithWindows;
        _openDiagnostics = openDiagnostics;
        _openLatestHotKey.Value = SavedHotKey.Normalize(
            openLatestFolderHotKey,
            SavedHotKey.OpenLatestFolderDefault());
        _copyLatestPathHotKey.Value = SavedHotKey.Normalize(
            copyLatestFolderPathHotKey,
            SavedHotKey.CopyLatestFolderPathDefault());

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
    public bool StartWithWindows => _startWithWindows.Checked;
    public SavedHotKey OpenLatestFolderHotKey => _openLatestHotKey.Value;
    public SavedHotKey CopyLatestFolderPathHotKey => _copyLatestPathHotKey.Value;

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

        var hotKeyPage = new TabPage("快捷键")
        {
            BackColor = Theme.Window,
            ForeColor = Theme.Text,
            Padding = new Padding(8)
        };
        tabs.TabPages.Add(hotKeyPage);
        BuildHotKeyPage(hotKeyPage);

        var diagnosticsPage = new TabPage("诊断")
        {
            BackColor = Theme.Window,
            ForeColor = Theme.Text,
            Padding = new Padding(8)
        };
        tabs.TabPages.Add(diagnosticsPage);
        BuildDiagnosticsPage(diagnosticsPage);

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
        okButton.Click += (_, _) =>
        {
            if (Save())
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
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
            Text = "收藏从下往上按最近更新时间排列。",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        root.SetColumnSpan(description, 2);
        root.Controls.Add(description, 0, 1);
    }

    private void BuildHotKeyPage(Control page)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 176,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(4, 14, 4, 0),
            BackColor = Theme.Window
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        page.Controls.Add(root);

        root.Controls.Add(CreateHotKeyLabel("打开最新文件夹"), 0, 0);
        root.Controls.Add(_openLatestHotKey.Control, 1, 0);
        root.Controls.Add(CreateHotKeyLabel("复制最新文件夹地址"), 0, 1);
        root.Controls.Add(_copyLatestPathHotKey.Control, 1, 1);

        var hint = new Label
        {
            Text = "至少勾选一个修饰键，字母范围为 A-Z。",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.SetColumnSpan(hint, 2);
        root.Controls.Add(hint, 0, 2);

        _hotKeyError.Dock = DockStyle.Fill;
        _hotKeyError.ForeColor = Color.FromArgb(248, 113, 113);
        _hotKeyError.TextAlign = ContentAlignment.MiddleLeft;
        _hotKeyError.AutoEllipsis = true;
        root.SetColumnSpan(_hotKeyError, 2);
        root.Controls.Add(_hotKeyError, 0, 3);
    }

    private static Label CreateHotKeyLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = Theme.Text,
        TextAlign = ContentAlignment.MiddleLeft
    };

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

    private void BuildDiagnosticsPage(Control page)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 126,
            RowCount = 3,
            Padding = new Padding(8, 16, 8, 0),
            BackColor = Theme.Window
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        page.Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "查看每个磁盘的连接状态、最近事件和错误日志。",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var button = CreateButton("打开诊断信息", primary: false);
        button.AutoSize = true;
        button.Enabled = _openDiagnostics is not null;
        button.Click += (_, _) => _openDiagnostics?.Invoke();
        root.Controls.Add(button, 0, 1);

        root.Controls.Add(new Label
        {
            Text = AppLog.LogDirectory,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = Theme.Dim,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 2);
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

    private bool Save()
    {
        if (!TryValidateHotKeys(out var error))
        {
            _hotKeyError.Text = error;
            return false;
        }

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

        _hotKeyError.Text = string.Empty;
        return true;
    }

    private bool TryValidateHotKeys(out string error)
    {
        var openLatest = OpenLatestFolderHotKey;
        if (!openLatest.TryValidate(out var openError))
        {
            error = $"打开最新文件夹：{openError}";
            return false;
        }

        var copyLatest = CopyLatestFolderPathHotKey;
        if (!copyLatest.TryValidate(out var copyError))
        {
            error = $"复制最新文件夹地址：{copyError}";
            return false;
        }

        if (openLatest.SameCombination(copyLatest))
        {
            error = "两个快捷键不能使用相同组合";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private sealed class HotKeyEditor
    {
        private readonly CheckBox _ctrl = CreateModifier("Ctrl");
        private readonly CheckBox _alt = CreateModifier("Alt");
        private readonly CheckBox _shift = CreateModifier("Shift");
        private readonly ComboBox _key = new();

        public HotKeyEditor()
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Window,
                Padding = new Padding(0, 7, 0, 0)
            };
            row.Controls.Add(_ctrl);
            row.Controls.Add(_alt);
            row.Controls.Add(_shift);

            _key.DropDownStyle = ComboBoxStyle.DropDownList;
            _key.Width = 54;
            _key.Height = 26;
            _key.Margin = new Padding(8, 1, 0, 0);
            _key.BackColor = Theme.Panel;
            _key.ForeColor = Theme.Text;
            _key.Items.AddRange(Enumerable.Range('A', 26).Select(value => ((char)value).ToString()).Cast<object>().ToArray());
            row.Controls.Add(_key);
            Control = row;
        }

        public Control Control { get; }

        public SavedHotKey Value
        {
            get => new()
            {
                Ctrl = _ctrl.Checked,
                Alt = _alt.Checked,
                Shift = _shift.Checked,
                Key = _key.SelectedItem?.ToString() ?? string.Empty
            };
            set
            {
                _ctrl.Checked = value.Ctrl;
                _alt.Checked = value.Alt;
                _shift.Checked = value.Shift;
                _key.SelectedItem = value.Key.Trim().ToUpperInvariant();
                if (_key.SelectedIndex < 0)
                {
                    _key.SelectedIndex = 0;
                }
            }
        }

        private static CheckBox CreateModifier(string text) => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = Theme.Text,
            BackColor = Theme.Window,
            Margin = new Padding(0, 3, 10, 0)
        };
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
