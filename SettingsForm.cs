namespace DropSpot;

public sealed class SettingsForm : Form
{
    private readonly CheckedListBox _driveList = new();
    private readonly ListBox _excludeList = new();
    private readonly NumericUpDown _floatingFavoriteCount = new();
    private readonly CheckBox _startWithWindows = new ToggleSwitch();
    private readonly CheckBox _selectLatestFile = new ToggleSwitch();
    private readonly CheckBox _filterCommonNoise = new ToggleSwitch();
    private readonly CheckBox _filterDevFiles = new ToggleSwitch();
    private readonly ValueSlider _opacitySlider = new() { Minimum = 50, Maximum = 100 };
    private readonly Label _opacityValue = new();
    private readonly Label _pageTitle = new();
    private readonly Label _pageDescription = new();
    private readonly Panel _pageHost = new();
    private readonly List<(NavButton Button, Control Page, string Title, string Description)> _pages = new();
    private readonly Action<int>? _previewOpacity;
    private readonly TextBox _hiddenExtensions = new();
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
        SavedHotKey? copyLatestFolderPathHotKey = null,
        bool filterCommonNoise = true,
        bool selectLatestFileWhenOpeningFolder = true,
        bool filterDevFiles = true,
        IEnumerable<string>? hiddenExtensions = null,
        int capsuleOpacity = AppSettings.DefaultCapsuleOpacity,
        Action<int>? previewOpacity = null)
    {
        _previewOpacity = previewOpacity;
        _opacitySlider.Value = AppSettings.NormalizeCapsuleOpacity(capsuleOpacity);
        _opacityValue.Text = $"{_opacitySlider.Value}%";
        _opacitySlider.ValueChanged += (_, _) =>
        {
            _opacityValue.Text = $"{_opacitySlider.Value}%";
            _previewOpacity?.Invoke(_opacitySlider.Value);
        };
        _filterDevFiles.Checked = filterDevFiles;
        _hiddenExtensions.Text = string.Join(" ", PathRules.ParseExtensions(hiddenExtensions));
        _filterCommonNoise.Checked = filterCommonNoise;
        _selectLatestFile.Checked = selectLatestFileWhenOpeningFolder;
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

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "DropSpot 设置";
        Icon = AppIcon.Create();
        ClientSize = new Size(800, 600);
        MinimumSize = new Size(720, 540);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = SettingsPalette.Content;
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
    public int CapsuleOpacity => _opacitySlider.Value;
    public bool FilterCommonNoise => _filterCommonNoise.Checked;
    public bool SelectLatestFileWhenOpeningFolder => _selectLatestFile.Checked;
    public bool FilterDevFiles => _filterDevFiles.Checked;
    public IReadOnlyList<string> HiddenExtensions => PathRules.ParseExtensions(new[] { _hiddenExtensions.Text });
    public SavedHotKey OpenLatestFolderHotKey => _openLatestHotKey.Value;
    public SavedHotKey CopyLatestFolderPathHotKey => _copyLatestPathHotKey.Value;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.ApplyDarkTitleBar(this);
    }

    private void BuildUi()
    {
        var nav = new Panel
        {
            Dock = DockStyle.Left,
            Width = 188,
            BackColor = SettingsPalette.Nav,
            Padding = new Padding(0, 18, 0, 12)
        };

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SettingsPalette.Content
        };

        Controls.Add(content);
        Controls.Add(nav);

        // ---- 左侧：标题 + 导航 ----
        var navList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = SettingsPalette.Nav,
            Padding = new Padding(0)
        };
        nav.Controls.Add(navList);

        var brand = new Label
        {
            Text = "DropSpot 设置",
            AutoSize = false,
            Size = new Size(188, 44),
            Padding = new Padding(22, 0, 0, 8),
            ForeColor = Theme.Text,
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 10)
        };
        navList.Controls.Add(brand);

        // ---- 右侧：标题区 + 页面 + 底部按钮 ----
        var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = SettingsPalette.Content, Padding = new Padding(32, 26, 32, 0) };
        _pageTitle.Dock = DockStyle.Top;
        _pageTitle.Height = 30;
        _pageTitle.ForeColor = Theme.Text;
        _pageTitle.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
        _pageDescription.Dock = DockStyle.Top;
        _pageDescription.Height = 24;
        _pageDescription.ForeColor = SettingsPalette.Muted;
        header.Controls.Add(_pageDescription);
        header.Controls.Add(_pageTitle);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = SettingsPalette.Content, Padding = new Padding(32, 14, 24, 18) };
        var saveButton = CreateButton("保存", primary: true);
        saveButton.Dock = DockStyle.Right;
        saveButton.Click += (_, _) =>
        {
            if (Save())
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        var cancelButton = CreateButton("取消", primary: false);
        cancelButton.Dock = DockStyle.Right;
        cancelButton.DialogResult = DialogResult.Cancel;
        var spacer = new Panel { Dock = DockStyle.Right, Width = 8, BackColor = SettingsPalette.Content };
        _hotKeyError.Dock = DockStyle.Fill;
        _hotKeyError.ForeColor = SettingsPalette.Danger;
        _hotKeyError.TextAlign = ContentAlignment.MiddleLeft;
        _hotKeyError.AutoEllipsis = true;
        footer.Controls.Add(_hotKeyError);
        footer.Controls.Add(cancelButton);
        footer.Controls.Add(spacer);
        footer.Controls.Add(saveButton);
        footer.Paint += (_, e) =>
        {
            using var line = new Pen(Color.FromArgb(30, 42, 58));
            e.Graphics.DrawLine(line, 32, 0, footer.Width - 24, 0);
        };

        _pageHost.Dock = DockStyle.Fill;
        _pageHost.BackColor = SettingsPalette.Content;
        _pageHost.Padding = new Padding(32, 4, 24, 8);

        content.Controls.Add(_pageHost);
        content.Controls.Add(footer);
        content.Controls.Add(header);

        AddPage(navList, "常规", "", "启动方式、打开文件夹的行为和悬浮舱外观。", BuildGeneralPage());
        AddPage(navList, "监视磁盘", "", "勾选要监视的磁盘。只支持 NTFS / ReFS。", BuildDrivePage());
        AddPage(navList, "过滤", "", "不想看到的文件夹和文件，不会出现在悬浮舱里。", BuildFilterPage());
        AddPage(navList, "快捷键", "", "在任何地方都能用的全局快捷键。", BuildHotKeyPage());
        AddPage(navList, "关于与诊断", "", "版本信息、监视状态和日志。", BuildAboutPage());
        SelectPage(0);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private void AddPage(Control navList, string title, string glyph, string description, Control page)
    {
        var index = _pages.Count;
        var button = new NavButton(title, glyph)
        {
            Width = 188,
            Margin = new Padding(0, 0, 0, 2)
        };
        button.Click += (_, _) => SelectPage(index);
        navList.Controls.Add(button);
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _pageHost.Controls.Add(page);
        _pages.Add((button, page, title, description));
    }

    private void SelectPage(int index)
    {
        for (var i = 0; i < _pages.Count; i++)
        {
            var (button, page, title, description) = _pages[i];
            var selected = i == index;
            button.Selected = selected;
            page.Visible = selected;
            if (selected)
            {
                _pageTitle.Text = title;
                _pageDescription.Text = description;
                page.BringToFront();
            }
        }
    }

    /// <summary>一个可滚动的纵向页面：内容按行往下排。</summary>
    private static (Panel Page, TableLayoutPanel Rows) CreatePage()
    {
        var page = new Panel { AutoScroll = true, BackColor = SettingsPalette.Content };
        var rows = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = SettingsPalette.Content,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        page.Controls.Add(rows);
        return (page, rows);
    }

    private static void AddRow(TableLayoutPanel rows, Control control, int topMargin = 0)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, topMargin, 0, 0);
        rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rows.Controls.Add(control, 0, rows.RowCount++);
    }

    private static Label SectionTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SettingsPalette.Faint,
        Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
        Padding = new Padding(0, 0, 0, 4)
    };

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(500, 0),
        ForeColor = SettingsPalette.Muted,
        Padding = new Padding(46, 0, 0, 2)
    };

    private static void StyleToggle(CheckBox toggle, string text)
    {
        toggle.Text = text;
        toggle.Height = 30;
        toggle.BackColor = SettingsPalette.Content;
        toggle.ForeColor = Theme.Text;
    }

    private Control BuildGeneralPage()
    {
        var (page, rows) = CreatePage();

        AddRow(rows, SectionTitle("启动"));
        StyleToggle(_startWithWindows, "随 Windows 启动");
        AddRow(rows, _startWithWindows);
        AddRow(rows, Hint("登录后自动开始监视，悬浮舱保持收起。磁盘监视由已授权的后台进程完成，开机不会弹出授权确认。"));

        AddRow(rows, SectionTitle("打开文件夹"), 22);
        StyleToggle(_selectLatestFile, "打开文件夹时选中最新文件");
        AddRow(rows, _selectLatestFile);
        AddRow(rows, Hint("点活跃文件夹、收藏夹或用快捷键打开时，资源管理器会直接定位到刚写入的文件。"));

        AddRow(rows, SectionTitle("悬浮舱外观"), 22);
        var opacityRow = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Height = 34,
            AutoSize = false,
            BackColor = SettingsPalette.Content
        };
        opacityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        opacityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        opacityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
        opacityRow.Controls.Add(new Label
        {
            Text = "背景不透明度",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _opacitySlider.Dock = DockStyle.Fill;
        _opacitySlider.Margin = new Padding(0, 3, 8, 3);
        opacityRow.Controls.Add(_opacitySlider, 1, 0);
        _opacityValue.Dock = DockStyle.Fill;
        _opacityValue.ForeColor = Theme.Text;
        _opacityValue.TextAlign = ContentAlignment.MiddleRight;
        opacityRow.Controls.Add(_opacityValue, 2, 0);
        AddRow(rows, opacityRow, 2);
        AddRow(rows, new Label
        {
            Text = "拖动时悬浮舱会实时变化。文字和图标始终保持清晰，只有底色变透明。",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            ForeColor = SettingsPalette.Muted,
            Padding = new Padding(0, 2, 0, 0)
        });
        return page;
    }

    private Control BuildDrivePage()
    {
        var page = new Panel { BackColor = SettingsPalette.Content, Padding = new Padding(0, 0, 8, 0) };
        _driveList.Dock = DockStyle.Fill;
        _driveList.CheckOnClick = true;
        _driveList.BackColor = SettingsPalette.Card;
        _driveList.ForeColor = Theme.Text;
        _driveList.BorderStyle = BorderStyle.None;
        _driveList.IntegralHeight = false;
        _driveList.Font = new Font("Microsoft YaHei UI", 10F);
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = SettingsPalette.Card, Padding = new Padding(12, 10, 12, 10) };
        frame.Controls.Add(_driveList);
        var note = new Label
        {
            Text = "网络盘、光驱和 FAT32 / exFAT 分区无法使用 USN 日志，不会出现在这里或无法勾选。",
            Dock = DockStyle.Bottom,
            Height = 40,
            ForeColor = SettingsPalette.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        page.Controls.Add(frame);
        page.Controls.Add(note);
        return page;
    }

    private Control BuildFilterPage()
    {
        var page = new Panel { BackColor = SettingsPalette.Content, Padding = new Padding(0, 0, 8, 0) };
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = SettingsPalette.Content
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        AddRow(top, SectionTitle("自动过滤"));
        StyleToggle(_filterCommonNoise, "过滤常见噪音目录");
        AddRow(top, _filterCommonNoise);
        AddRow(top, Hint(".git、node_modules、浏览器缓存、.claude / .cursor 等工具目录"));
        StyleToggle(_filterDevFiles, "隐藏开发 / AI 编程产生的文件");
        AddRow(top, _filterDevFiles, 6);
        AddRow(top, Hint("编译产物、缓存、日志、锁文件（.pyc .obj .pdb .map .log .lock …）和 obj 目录"));

        AddRow(top, SectionTitle("额外隐藏的扩展名"), 18);
        _hiddenExtensions.BackColor = SettingsPalette.Card;
        _hiddenExtensions.ForeColor = Theme.Text;
        _hiddenExtensions.BorderStyle = BorderStyle.FixedSingle;
        _hiddenExtensions.PlaceholderText = "例如 .tmp .log .bak，空格或逗号分隔";
        _hiddenExtensions.Font = new Font("Microsoft YaHei UI", 10F);
        AddRow(top, _hiddenExtensions);

        AddRow(top, SectionTitle("排除的文件夹"), 18);
        AddRow(top, new Label
        {
            Text = "完整路径排除该文件夹及子文件夹；名称或通配符（如 node_modules、*_temp_*）在任意层级匹配。",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            ForeColor = SettingsPalette.Muted,
            Padding = new Padding(0, 0, 0, 6)
        });

        _excludeList.Dock = DockStyle.Fill;
        _excludeList.BackColor = SettingsPalette.Card;
        _excludeList.ForeColor = Theme.Text;
        _excludeList.BorderStyle = BorderStyle.None;
        _excludeList.IntegralHeight = false;
        _excludeList.SelectionMode = SelectionMode.MultiExtended;
        var listFrame = new Panel { Dock = DockStyle.Fill, BackColor = SettingsPalette.Card, Padding = new Padding(10, 8, 10, 8), MinimumSize = new Size(0, 90) };
        listFrame.Controls.Add(_excludeList);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = SettingsPalette.Content,
            Padding = new Padding(0, 8, 0, 0)
        };
        var addFolder = CreateButton("添加文件夹…", primary: false);
        addFolder.Click += (_, _) => AddExcludeFolder();
        var addName = CreateButton("按名称添加…", primary: false);
        addName.Click += (_, _) => AddExcludeNameRule();
        var remove = CreateButton("移除所选", primary: false);
        remove.Click += (_, _) => RemoveSelectedExclusion();
        buttons.Controls.Add(addFolder);
        buttons.Controls.Add(addName);
        buttons.Controls.Add(remove);

        page.Controls.Add(listFrame);
        page.Controls.Add(buttons);
        page.Controls.Add(top);
        return page;
    }

    private Control BuildHotKeyPage()
    {
        var (page, rows) = CreatePage();
        AddRow(rows, SectionTitle("全局快捷键"));
        AddRow(rows, CreateHotKeyRow("打开最新文件夹", "打开并选中最近写入的文件", _openLatestHotKey), 4);
        AddRow(rows, CreateHotKeyRow("复制最新文件夹地址", "把完整路径复制到剪贴板", _copyLatestPathHotKey), 8);
        AddRow(rows, new Label
        {
            Text = "至少勾选一个修饰键，字母范围为 A-Z。如果和其他软件冲突，保存时会提示并恢复原来的快捷键。",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            ForeColor = SettingsPalette.Muted,
            Padding = new Padding(0, 12, 0, 0)
        });
        return page;
    }

    private static Control CreateHotKeyRow(string title, string description, HotKeyEditor editor)
    {
        var card = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 2,
            Height = 64,
            BackColor = SettingsPalette.Card,
            Padding = new Padding(14, 8, 12, 8)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
        card.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft,
            BackColor = SettingsPalette.Card
        }, 0, 0);
        card.Controls.Add(new Label
        {
            Text = description,
            Dock = DockStyle.Fill,
            ForeColor = SettingsPalette.Muted,
            TextAlign = ContentAlignment.TopLeft,
            BackColor = SettingsPalette.Card
        }, 0, 1);
        editor.Control.Dock = DockStyle.Fill;
        card.Controls.Add(editor.Control, 1, 0);
        card.SetRowSpan(editor.Control, 2);
        return card;
    }

    private Control BuildAboutPage()
    {
        var (page, rows) = CreatePage();
        var brand = new Panel { Height = 86, BackColor = SettingsPalette.Card, Padding = new Padding(18, 14, 18, 14) };
        var name = new Label
        {
            Text = "DropSpot",
            Dock = DockStyle.Top,
            Height = 30,
            ForeColor = Theme.Text,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            BackColor = SettingsPalette.Card
        };
        var version = new Label
        {
            Text = $"版本：{Application.ProductVersion}",
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = SettingsPalette.Muted,
            BackColor = SettingsPalette.Card
        };
        var author = new Label
        {
            Text = "开发者：cslm",
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = SettingsPalette.Muted,
            BackColor = SettingsPalette.Card
        };
        brand.Controls.Add(author);
        brand.Controls.Add(version);
        brand.Controls.Add(name);
        AddRow(rows, brand);

        AddRow(rows, SectionTitle("诊断"), 22);
        AddRow(rows, new Label
        {
            Text = "查看每个磁盘的监视状态、后台监视进程是否连上、最近事件和错误日志。",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            ForeColor = SettingsPalette.Muted
        });
        var actions = new FlowLayoutPanel
        {
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = SettingsPalette.Content,
            Padding = new Padding(0, 6, 0, 0)
        };
        var diagnostics = CreateButton("打开诊断信息", primary: false);
        diagnostics.Enabled = _openDiagnostics is not null;
        diagnostics.Click += (_, _) => _openDiagnostics?.Invoke();
        var logs = CreateButton("打开日志文件夹", primary: false);
        logs.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(AppLog.LogDirectory);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{AppLog.LogDirectory}\"") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                AppLog.Warning($"打开日志文件夹失败：{ex.Message}");
            }
        };
        actions.Controls.Add(diagnostics);
        actions.Controls.Add(logs);
        AddRow(rows, actions);
        AddRow(rows, new Label
        {
            Text = AppLog.LogDirectory,
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            ForeColor = Theme.Dim,
            Padding = new Padding(0, 4, 0, 0)
        });
        return page;
    }

    private static Button CreateButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 32),
            Padding = new Padding(10, 0, 10, 0),
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? SettingsPalette.GreenDark : SettingsPalette.Card,
            ForeColor = primary ? Color.White : Theme.Text,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = primary ? SettingsPalette.GreenDark : SettingsPalette.CardBorder;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(36, 132, 86) : SettingsPalette.Hover;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(24, 96, 62) : SettingsPalette.Hover;
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

    private void AddExcludeNameRule()
    {
        using var dialog = new NameRuleInputForm();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var rule = PathRules.NormalizeRule(dialog.Rule);
        if (string.IsNullOrWhiteSpace(rule)
            || _excludeList.Items.Cast<object>().Any(item =>
                string.Equals(item.ToString(), rule, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _excludeList.Items.Add(rule);
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
                BackColor = SettingsPalette.Card,
                Padding = new Padding(0, 14, 0, 0)
            };
            row.Controls.Add(_ctrl);
            row.Controls.Add(_alt);
            row.Controls.Add(_shift);

            _key.DropDownStyle = ComboBoxStyle.DropDownList;
            _key.Width = 54;
            _key.Height = 26;
            _key.Margin = new Padding(8, 1, 0, 0);
            _key.BackColor = SettingsPalette.Content;
            _key.FlatStyle = FlatStyle.Flat;
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
            BackColor = SettingsPalette.Card,
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

internal sealed class NameRuleInputForm : Form
{
    private readonly TextBox _input = new();

    public NameRuleInputForm()
    {
        Text = "按名称排除";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 150);
        BackColor = SettingsPalette.Content;
        ForeColor = Theme.Text;
        Font = new Font("Microsoft YaHei UI", 9F);

        var hint = new Label
        {
            Text = "输入文件夹名称或通配符，例如：node_modules、.cache、*_temp_*\r\n路径中任意一级目录名匹配就会被排除。",
            Location = new Point(14, 12),
            Size = new Size(352, 42),
            ForeColor = SettingsPalette.Muted
        };
        Controls.Add(hint);

        _input.Location = new Point(14, 62);
        _input.Size = new Size(352, 26);
        _input.BackColor = SettingsPalette.Card;
        _input.ForeColor = Theme.Text;
        _input.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_input);

        var ok = new Button
        {
            Text = "添加",
            DialogResult = DialogResult.OK,
            Location = new Point(206, 106),
            Size = new Size(76, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = SettingsPalette.GreenDark,
            ForeColor = Color.White
        };
        ok.FlatAppearance.BorderColor = SettingsPalette.GreenDark;
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_input.Text))
            {
                DialogResult = DialogResult.None;
            }
        };
        Controls.Add(ok);

        var cancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(290, 106),
            Size = new Size(76, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = SettingsPalette.Card,
            ForeColor = Theme.Text
        };
        cancel.FlatAppearance.BorderColor = SettingsPalette.CardBorder;
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.ApplyDarkTitleBar(this);
    }

    public string Rule => _input.Text.Trim();
}
