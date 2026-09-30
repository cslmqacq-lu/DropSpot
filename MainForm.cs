using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DropSpot;

public sealed class MainForm : Form
{
    private const int MaxFolders = 6;
    private const int MaxPendingRecords = 512;
    private const int MaxRecordsPerFlush = 128;
    private const int WmSysCommand = 0x0112;
    private const int ScRestore = 0xF120;
    private const int SysCommandMask = 0xFFF0;
    private const string PlayIcon = "\uE768";
    private const string PauseIcon = "\uE769";
    private const string ClearIcon = "\uE74D";
    private const string SettingsIcon = "\uE713";
    private const string AddIcon = "\uE710";
    private const string FloatingIcon = "\uE8A7";

    private readonly FileMonitorService _monitor = new();
    private readonly Dictionary<string, FolderActivity> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FolderCard> _folderCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly FavoriteFolderStore _favorites = new();
    private readonly PinnedFolderStore _pinnedFolders = new();
    private readonly ActivityHistoryStore _activityHistory = new();
    private readonly Dictionary<string, FavoriteFolderCard> _favoriteCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PinnedFolderForm> _pinnedFolderForms = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _openingFolderPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WatchScope> _watchScopes = new();
    private readonly List<string> _excludedPaths = new();
    private readonly PendingRecordBuffer _pendingRecords = new(MaxPendingRecords);
    private readonly System.Windows.Forms.Timer _uiFlushTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _favoriteSaveTimer = new() { Interval = 30000 };
    private readonly AppSettings _settings;
    private readonly ToolTip _toolTip = new();

    private readonly FlickerFreeFlowLayoutPanel _folderList = new();
    private readonly FlickerFreeFlowLayoutPanel _favoriteList = new();
    private readonly Panel _favoriteHost = new();
    private readonly Label _statusLabel = new();
    private readonly Panel _elevationBanner = new();
    private TableLayoutPanel? _rootLayout;
    private bool _elevationBannerShown;
    private bool _elevationNoticeShown;
    private readonly Label _emptyLabel = new();
    private readonly Label _favoriteEmptyLabel = new();
    private readonly Button _toggleButton = new();
    private readonly Button _clearButton = new();
    private readonly Button _openFloatingButton = new();
    private readonly Button _addFavoriteButton = new();
    private readonly Button _activeTabButton = new();
    private readonly Button _favoriteTabButton = new();
    private readonly string? _settingsWarning;
    private readonly bool _startMinimized;
    private FloatingFolderForm? _floatingForm;
    private TrayIconController? _trayIcon;
    private GlobalHotKeyManager? _hotKeys;
    private FolderActivity? _latestFolder;
    private ChangeRecord? _latestFile;
    private int _latestFileSelectionGeneration;
    private bool _isMonitoring;
    private bool _isClosing;
    private bool _showFavorites;
    private bool _favoritesDirty;
    private bool _floatingModeActive;
    private bool _wasNativeMinimized;
    private bool _restoreFloatingAfterTaskbarMinimize;
    private Rectangle _mainWindowRestoreBounds;
    private FormWindowState _mainWindowRestoreState = FormWindowState.Normal;

    public MainForm(bool startMinimized = false)
    {
        _startMinimized = startMinimized;
        Text = $"DropSpot v{Application.ProductVersion}";
        Icon = AppIcon.Create();
        MinimumSize = new Size(460, 500);
        Size = new Size(500, 610);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildUi();
        _settings = AppSettings.Load(out var settingsWarning);
        var startupApplied = StartupRegistration.TryApply(_settings.StartWithWindows, out var startupError);
        if (!string.IsNullOrWhiteSpace(startupError))
        {
            var startupMessage = startupApplied ? startupError : $"开机启动设置失败：{startupError}";
            AppLog.Warning(startupMessage);
            settingsWarning = string.IsNullOrWhiteSpace(settingsWarning)
                ? startupMessage
                : $"{settingsWarning}；{startupMessage}";
        }

        _settingsWarning = settingsWarning;
        _floatingForm = new FloatingFolderForm(
            OpenLatestFolder,
            OpenFile,
            OpenActivityFolder,
            RestoreMainWindow,
            MinimizeMainWindow,
            ToggleMonitor,
            Close,
            CopyPath,
            AddFavoritePath,
            PinFolderPath,
            SaveFloatingPosition,
            () => _isMonitoring,
            _pinnedFolders.Contains);
        _trayIcon = new TrayIconController(
            RestoreMainWindow,
            EnterFloatingMode,
            ToggleMonitor,
            ShowActivityHistory,
            ShowDiagnostics,
            Close,
            Elevation.IsElevated ? null : RestartAsAdministrator);
        LoadSavedSettings();
        PopulateDrives();
        if (_excludedPaths.Count == 0)
        {
            PopulateDefaultExclusions();
            SaveSettings();
        }

        _uiFlushTimer.Tick += (_, _) => FlushPendingRecords();
        _favoriteSaveTimer.Tick += (_, _) => PersistFavoriteActivity();
        _monitor.Changed += (_, record) => _pendingRecords.Add(record);
        _monitor.MonitorError += (_, message) => ShowMonitorError(message);
        _monitor.VolumeStatusChanged += (_, status) => OnVolumeStatusChanged(status);
        SizeChanged += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && !_isClosing)
            {
                if (IsUsableWindowBounds(RestoreBounds))
                {
                    _mainWindowRestoreBounds = RestoreBounds;
                }

                _wasNativeMinimized = true;
                return;
            }

            var restoredFromMinimized = _wasNativeMinimized;
            _wasNativeMinimized = false;
            CaptureMainWindowPlacement();
            if (restoredFromMinimized && !_floatingModeActive && !_isClosing)
            {
                BeginInvoke(RenderCurrentMainView);
            }
        };
        Move += (_, _) => CaptureMainWindowPlacement();
        FormClosing += (_, _) =>
        {
            _isClosing = true;
            _uiFlushTimer.Stop();
            _uiFlushTimer.Dispose();
            _favoriteSaveTimer.Stop();
            PersistFavoriteActivity();
            _favoriteSaveTimer.Dispose();
            _hotKeys?.Dispose();
            _hotKeys = null;
            _trayIcon?.Dispose();
            _trayIcon = null;
            _monitor.Dispose();
            AppLog.Info("主窗口关闭，监视服务已停止");
            _floatingForm?.Close();
            _floatingForm?.Dispose();
            _floatingForm = null;
            DisposeFolderCards();
            DisposeFavoriteCards();
            DisposePinnedFolderForms();
            ShellIconProvider.DisposeCache();
        };
        Shown += (_, _) =>
        {
            CaptureMainWindowPlacement();
            if (_watchScopes.Any(scope => scope.Enabled))
            {
                BeginInvoke(() =>
                {
                    _ = StartMonitor(promptIfMissing: false);
                    if (!string.IsNullOrWhiteSpace(_settingsWarning))
                    {
                        SetStatus(_settingsWarning);
                    }
                });
            }
            else if (!string.IsNullOrWhiteSpace(_settingsWarning))
            {
                SetStatus(_settingsWarning);
            }

            if (_startMinimized)
            {
                BeginInvoke(EnterFloatingMode);
            }
        };
        UpdateStatus();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        EnableDarkTitleBar();
        foreach (var error in RegisterConfiguredHotKeys())
        {
            AppLog.Warning(error);
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (_hotKeys?.ProcessMessage(message.Msg, message.WParam) == true)
        {
            message.Result = IntPtr.Zero;
            return;
        }

        if (!_isClosing
            && ShouldRestoreFloatingMode(
                _restoreFloatingAfterTaskbarMinimize,
                message.Msg,
                message.WParam))
        {
            _restoreFloatingAfterTaskbarMinimize = false;
            message.Result = IntPtr.Zero;
            BeginInvoke(EnterFloatingMode);
            return;
        }

        base.WndProc(ref message);
    }

    internal void ActivateFromExternalRequest()
    {
        RestoreMainWindow();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0),
            BackColor = Theme.Window
        };
        root.RowCount = 4;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);
        _rootLayout = root;
        BuildElevationBanner();
        root.Controls.Add(_elevationBanner, 0, 1);

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            Padding = new Padding(12, 8, 10, 8)
        };
        root.Controls.Add(header, 0, 0);

        var statusHost = new Panel
        {
            BackColor = Color.Black,
        };
        header.Controls.Add(statusHost);

        var statusDot = new Panel
        {
            BackColor = Theme.Dim,
            Location = new Point(4, 14),
            Size = new Size(8, 8)
        };
        statusHost.Controls.Add(statusDot);

        _statusLabel.AutoSize = false;
        _statusLabel.ForeColor = Theme.Muted;
        _statusLabel.BackColor = Color.Black;
        _statusLabel.Font = new Font("Microsoft YaHei UI", 8.8F);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Location = new Point(22, 5);
        _statusLabel.Height = 26;
        _statusLabel.AutoEllipsis = true;
        statusHost.Controls.Add(_statusLabel);

        var actionHost = new Panel
        {
            BackColor = Color.Black
        };
        header.Controls.Add(actionHost);

        _toggleButton.Text = PlayIcon;
        _toggleButton.Tag = statusDot;
        StyleIconButton(_toggleButton, primary: true);
        _toggleButton.Location = new Point(0, 1);
        _toggleButton.Click += (_, _) => ToggleMonitor();
        actionHost.Controls.Add(_toggleButton);
        _toolTip.SetToolTip(_toggleButton, "开始监视");

        _clearButton.Text = ClearIcon;
        StyleIconButton(_clearButton, primary: false);
        _clearButton.Location = new Point(42, 1);
        _clearButton.Click += (_, _) => ClearActivities();
        actionHost.Controls.Add(_clearButton);
        _toolTip.SetToolTip(_clearButton, "清空活跃列表");

        var settingsButton = new Button
        {
            Text = SettingsIcon
        };
        StyleIconButton(settingsButton, primary: false);
        settingsButton.Location = new Point(84, 1);
        settingsButton.Click += (_, _) => OpenSettings();
        actionHost.Controls.Add(settingsButton);
        _toolTip.SetToolTip(settingsButton, "设置");

        _openFloatingButton.Name = "openFloatingButton";
        _openFloatingButton.Text = FloatingIcon;
        StyleIconButton(_openFloatingButton, primary: false);
        _openFloatingButton.Location = new Point(126, 1);
        _openFloatingButton.Click += (_, _) => EnterFloatingMode();
        actionHost.Controls.Add(_openFloatingButton);
        _toolTip.SetToolTip(_openFloatingButton, "打开悬浮窗");

        void LayoutHeader()
        {
            const int actionWidth = 160;
            actionHost.Bounds = new Rectangle(
                Math.Max(12, header.ClientSize.Width - 10 - actionWidth),
                8,
                actionWidth,
                36);

            statusHost.Bounds = new Rectangle(
                12,
                8,
                Math.Max(90, actionHost.Left - 20),
                36);
            _statusLabel.Width = Math.Max(60, statusHost.ClientSize.Width - 26);
        }

        header.Resize += (_, _) => LayoutHeader();
        LayoutHeader();

        var tabHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window,
            Padding = new Padding(10, 5, 10, 5)
        };
        root.Controls.Add(tabHost, 0, 2);

        ConfigureTabButton(_activeTabButton, "活跃", selected: true);
        _activeTabButton.Location = new Point(10, 5);
        _activeTabButton.Click += (_, _) => SetFolderView(showFavorites: false);
        tabHost.Controls.Add(_activeTabButton);

        ConfigureTabButton(_favoriteTabButton, "收藏", selected: false);
        _favoriteTabButton.Location = new Point(102, 5);
        _favoriteTabButton.Click += (_, _) => SetFolderView(showFavorites: true);
        tabHost.Controls.Add(_favoriteTabButton);

        var listHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window,
            Padding = new Padding(10, 10, 10, 10)
        };
        root.Controls.Add(listHost, 0, 3);

        _folderList.Dock = DockStyle.Fill;
        _folderList.FlowDirection = FlowDirection.TopDown;
        _folderList.WrapContents = false;
        _folderList.AutoScroll = true;
        _folderList.BackColor = Theme.Window;
        _folderList.Resize += (_, _) => RenderFolders();
        listHost.Controls.Add(_folderList);

        _favoriteHost.Dock = DockStyle.Fill;
        _favoriteHost.BackColor = Theme.Window;
        _favoriteHost.Visible = false;
        listHost.Controls.Add(_favoriteHost);

        var favoriteLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Theme.Window
        };
        favoriteLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        favoriteLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        favoriteLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        _favoriteHost.Controls.Add(favoriteLayout);

        var favoriteContentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window
        };
        favoriteLayout.Controls.Add(favoriteContentHost, 0, 0);

        _favoriteList.Dock = DockStyle.Fill;
        _favoriteList.FlowDirection = FlowDirection.TopDown;
        _favoriteList.WrapContents = false;
        _favoriteList.AutoScroll = true;
        _favoriteList.BackColor = Theme.Window;
        _favoriteList.Resize += (_, _) =>
        {
            if (_showFavorites)
            {
                RenderFavorites();
            }
        };
        favoriteContentHost.Controls.Add(_favoriteList);

        _favoriteEmptyLabel.Text = "暂无收藏文件夹";
        _favoriteEmptyLabel.Dock = DockStyle.Fill;
        _favoriteEmptyLabel.ForeColor = Theme.Muted;
        _favoriteEmptyLabel.BackColor = Theme.Window;
        _favoriteEmptyLabel.TextAlign = ContentAlignment.MiddleCenter;
        _favoriteEmptyLabel.Font = new Font("Microsoft YaHei UI", 10F);
        _favoriteEmptyLabel.Visible = false;
        favoriteContentHost.Controls.Add(_favoriteEmptyLabel);

        _addFavoriteButton.Text = AddIcon;
        _addFavoriteButton.Dock = DockStyle.Fill;
        _addFavoriteButton.Margin = Padding.Empty;
        _addFavoriteButton.FlatStyle = FlatStyle.Flat;
        _addFavoriteButton.FlatAppearance.BorderSize = 1;
        _addFavoriteButton.FlatAppearance.BorderColor = Theme.BorderStrong;
        _addFavoriteButton.FlatAppearance.MouseOverBackColor = Theme.Card;
        _addFavoriteButton.FlatAppearance.MouseDownBackColor = Theme.CardAlt;
        _addFavoriteButton.BackColor = Theme.Window;
        _addFavoriteButton.ForeColor = Theme.Text;
        _addFavoriteButton.Font = new Font("Segoe MDL2 Assets", 13F);
        _addFavoriteButton.TabStop = false;
        _addFavoriteButton.Click += (_, _) => OpenAddFavoriteDialog();
        favoriteLayout.Controls.Add(_addFavoriteButton, 0, 1);
        _toolTip.SetToolTip(_addFavoriteButton, "添加收藏文件夹");

        _emptyLabel.Text = "暂无活跃文件夹";
        _emptyLabel.Dock = DockStyle.Fill;
        _emptyLabel.ForeColor = Theme.Muted;
        _emptyLabel.BackColor = Theme.Window;
        _emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
        _emptyLabel.Font = new Font("Microsoft YaHei UI", 10F);
        listHost.Controls.Add(_emptyLabel);
        _emptyLabel.BringToFront();
    }

    private void BuildElevationBanner()
    {
        _elevationBanner.Dock = DockStyle.Fill;
        _elevationBanner.BackColor = Color.FromArgb(58, 44, 12);
        _elevationBanner.Padding = new Padding(12, 0, 10, 0);
        _elevationBanner.Visible = false;

        var message = new Label
        {
            Text = "读取磁盘 USN 日志需要管理员权限，当前无法监视。",
            ForeColor = Color.FromArgb(253, 224, 138),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Dock = DockStyle.Fill
        };

        var restartButton = new Button
        {
            Text = "以管理员重启",
            Dock = DockStyle.Right,
            Width = 104,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(120, 88, 20),
            ForeColor = Color.White,
            TabStop = false,
            UseVisualStyleBackColor = false
        };
        restartButton.FlatAppearance.BorderColor = Color.FromArgb(180, 132, 30);
        restartButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(150, 110, 26);
        restartButton.Click += (_, _) => RestartAsAdministrator();

        var buttonHost = new Panel
        {
            Dock = DockStyle.Right,
            Width = 110,
            Padding = new Padding(0, 5, 0, 5),
            BackColor = Color.Transparent
        };
        buttonHost.Controls.Add(restartButton);
        _elevationBanner.Controls.Add(message);
        _elevationBanner.Controls.Add(buttonHost);
        _toolTip.SetToolTip(restartButton, "以管理员身份重新启动 DropSpot，并改为管理员开机启动");
    }

    private void UpdateElevationBanner(IReadOnlyList<VolumeMonitorStatus> statuses)
    {
        var show = !Elevation.IsElevated && statuses.Any(status => status.AccessDenied);
        if (_elevationBannerShown == show || _rootLayout is null)
        {
            return;
        }

        _elevationBannerShown = show;
        _elevationBanner.Visible = show;
        _rootLayout.RowStyles[1].Height = show ? 38 : 0;

        // 悬浮窗模式或开机自启时看不到主窗口，用托盘气泡提示一次。
        if (show && !_elevationNoticeShown && (_floatingModeActive || !Visible))
        {
            _elevationNoticeShown = true;
            _trayIcon?.ShowNotice(
                "DropSpot 需要管理员权限",
                "读取磁盘 USN 日志需要管理员权限，点击此处以管理员身份重启。");
        }
    }

    private void RestartAsAdministrator()
    {
        if (_isClosing)
        {
            return;
        }

        if (Elevation.TryStartElevatedInstance(_floatingModeActive, out var error))
        {
            AppLog.Info("以管理员身份重启 DropSpot");
            Close();
            return;
        }

        SetStatus(string.IsNullOrWhiteSpace(error) ? "以管理员身份重启失败" : error);
    }

    private static void ConfigureTabButton(Button button, string text, bool selected)
    {
        button.Text = text;
        button.Size = new Size(84, 30);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.TabStop = false;
        button.Font = new Font("Microsoft YaHei UI", 9F, selected ? FontStyle.Bold : FontStyle.Regular);
        ApplyTabStyle(button, selected);
    }

    private static void ApplyTabStyle(Button button, bool selected)
    {
        button.BackColor = selected ? Theme.CardLatest : Theme.Window;
        button.ForeColor = selected ? Theme.Text : Theme.Muted;
        button.FlatAppearance.BorderColor = selected ? Theme.BorderStrong : Theme.Border;
    }

    private static void StyleIconButton(Button button, bool primary)
    {
        button.Size = new Size(34, 34);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Theme.AccentDark : Theme.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Theme.Accent : Theme.BorderStrong;
        button.FlatAppearance.MouseDownBackColor = Theme.CardAlt;
        button.BackColor = primary ? Theme.AccentDark : Theme.Panel;
        button.ForeColor = primary ? Color.White : Theme.Text;
        button.Font = new Font("Segoe MDL2 Assets", 11.5F);
        button.TabStop = false;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
    }

    private void PopulateDrives()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
        {
            if (drive.DriveType is DriveType.Fixed or DriveType.Removable)
            {
                AddOrUpdateScope(new WatchScope(drive.RootDirectory.FullName, enabled: false));
            }
        }
    }

    private void LoadSavedSettings()
    {
        foreach (var saved in _settings.WatchScopes)
        {
            if (!string.IsNullOrWhiteSpace(saved.Path))
            {
                AddOrUpdateScope(new WatchScope(saved.Path, saved.Enabled));
            }
        }

        foreach (var path in _settings.ExcludedPaths)
        {
            AddExcludePath(path);
        }

        _monitor.FilterCommonNoise = _settings.FilterCommonNoise;
        _favorites.Load(_settings.FavoriteFolders);
        _pinnedFolders.Load(_settings.PinnedFolders);
        _activityHistory.Load(_settings.ActivityHistory);
        _activityHistory.RemoveWhere(IsExcluded);
        foreach (var activity in _activityHistory.Items.Take(MaxFolders))
        {
            _folders[activity.FolderPath] = activity;
        }
        UpdateFavoriteTabText();
        UpdateFloatingFavorites();
        RenderFolders();
        UpdateLatestFolder();
    }

    private void PopulateDefaultExclusions()
    {
        AddExcludePath("$Recycle.Bin");
        AddExcludePath("System Volume Information");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            AddExcludePath(Path.Combine(localAppData, "Temp"));
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            AddExcludePath(Path.Combine(userProfile, "AppData", "Local", "Temp"));
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            AddExcludePath(windows);
        }
    }

    private void AddOrUpdateScope(WatchScope scope)
    {
        var existing = _watchScopes.FirstOrDefault(item => string.Equals(item.Path, scope.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Enabled = existing.Enabled || scope.Enabled;
            return;
        }

        _watchScopes.Add(scope);
    }

    private void AddExcludePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || _excludedPaths.Any(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _excludedPaths.Add(path);
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(
            _watchScopes,
            _excludedPaths,
            _settings.FloatingFavoriteCount,
            _settings.StartWithWindows,
            ShowDiagnostics,
            _settings.OpenLatestFolderHotKey,
            _settings.CopyLatestFolderPathHotKey,
            _settings.FilterCommonNoise,
            _settings.SelectLatestFileWhenOpeningFolder);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _watchScopes.Clear();
        _watchScopes.AddRange(dialog.WatchScopes.Select(scope => new WatchScope(scope.Path, scope.Enabled)));

        _excludedPaths.Clear();
        _excludedPaths.AddRange(dialog.ExcludedPaths);
        _settings.FloatingFavoriteCount = dialog.FloatingFavoriteCount;
        _settings.StartWithWindows = dialog.StartWithWindows;
        _settings.FilterCommonNoise = dialog.FilterCommonNoise;
        _settings.SelectLatestFileWhenOpeningFolder = dialog.SelectLatestFileWhenOpeningFolder;
        _monitor.FilterCommonNoise = _settings.FilterCommonNoise;
        var previousOpenHotKey = _settings.OpenLatestFolderHotKey.Clone();
        var previousCopyHotKey = _settings.CopyLatestFolderPathHotKey.Clone();
        _settings.OpenLatestFolderHotKey = dialog.OpenLatestFolderHotKey;
        _settings.CopyLatestFolderPathHotKey = dialog.CopyLatestFolderPathHotKey;
        var hotKeyErrors = RegisterConfiguredHotKeys();
        if (hotKeyErrors.Count > 0)
        {
            _settings.OpenLatestFolderHotKey = previousOpenHotKey;
            _settings.CopyLatestFolderPathHotKey = previousCopyHotKey;
            var restoreErrors = RegisterConfiguredHotKeys();
            foreach (var error in hotKeyErrors.Concat(restoreErrors))
            {
                AppLog.Warning(error);
            }

            var restoreMessage = restoreErrors.Count == 0
                ? "已恢复原快捷键，其他设置仍会保存。"
                : "原快捷键恢复失败：" + string.Join("；", restoreErrors);
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, hotKeyErrors) + Environment.NewLine + restoreMessage,
                "快捷键冲突",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        _ = StartupRegistration.TryApply(_settings.StartWithWindows, out var startupError);
        SaveSettings();
        UpdateFloatingFavorites();

        ApplyExclusions();
        if (_isMonitoring)
        {
            RestartMonitor();
        }
        else if (_watchScopes.Any(scope => scope.Enabled))
        {
            StartMonitor(promptIfMissing: false);
        }
        else
        {
            UpdateStatus();
        }

        if (!string.IsNullOrWhiteSpace(startupError))
        {
            SetStatus($"开机启动设置失败：{startupError}");
        }
    }

    private void ToggleMonitor()
    {
        if (_isMonitoring)
        {
            StopMonitor();
        }
        else
        {
            StartMonitor(promptIfMissing: true);
        }
    }

    private bool StartMonitor(bool promptIfMissing)
    {
        var scopes = _watchScopes.Where(scope => scope.Enabled).ToArray();
        if (scopes.Length == 0)
        {
            SetStatus("请先选择硬盘");
            if (promptIfMissing)
            {
                OpenSettings();
            }
            return false;
        }

        _monitor.Start(scopes, _excludedPaths);
        _isMonitoring = _monitor.ActiveScopes.Count > 0;
        if (_isMonitoring)
        {
            _uiFlushTimer.Start();
        }
        _toggleButton.Text = _isMonitoring ? PauseIcon : PlayIcon;
        _toggleButton.BackColor = _isMonitoring ? Theme.Panel : Theme.AccentDark;
        _toolTip.SetToolTip(_toggleButton, _isMonitoring ? "暂停监视" : "开始监视");
        UpdateStatus();
        return _isMonitoring;
    }

    private void StopMonitor()
    {
        _uiFlushTimer.Stop();
        _monitor.Stop();
        FlushPendingRecords();
        _isMonitoring = false;
        _toggleButton.Text = PlayIcon;
        _toggleButton.BackColor = Theme.AccentDark;
        _toolTip.SetToolTip(_toggleButton, "开始监视");
        UpdateStatus();
    }

    private void RestartMonitor()
    {
        _uiFlushTimer.Stop();
        _monitor.Stop();
        ClearPendingRecords();
        _isMonitoring = false;
        StartMonitor(promptIfMissing: false);
    }

    private void ClearActivities()
    {
        ClearPendingRecords();
        _folders.Clear();
        _activityHistory.Clear();
        _expandedFolders.Clear();
        MarkFavoritesDirty();
        RenderFolders();
        UpdateLatestFolder();
        SetStatus("已清空");
    }

    private void FlushPendingRecords()
    {
        if (_folderList.IsDisposed)
        {
            return;
        }

        var records = _pendingRecords.DrainLatest(MaxRecordsPerFlush);
        if (records.Count == 0)
        {
            RefreshCardTimes();
            return;
        }

        FolderActivity? latestFolder = null;
        var favoriteActivityChanged = false;
        var pinnedActivityChanged = false;
        foreach (var record in records)
        {
            var folder = AddRecord(record);
            latestFolder = folder ?? latestFolder;
            if (folder is not null && _favorites.MarkActivity(record.FolderPath, record.Time))
            {
                favoriteActivityChanged = true;
            }
            if (folder is not null && _pinnedFolders.MarkActivity(record.FolderPath, record.Time))
            {
                pinnedActivityChanged = true;
            }
        }

        if (latestFolder is null)
        {
            return;
        }

        TrimFolders();
        MarkFavoritesDirty();
        RenderFolders();
        if (favoriteActivityChanged)
        {
            MarkFavoritesDirty();
            UpdateFloatingFavorites();
            if (_showFavorites)
            {
                RenderFavorites();
            }
        }
        if (pinnedActivityChanged)
        {
            SyncPinnedFolderForms();
        }
        UpdateLatestFolder();
        SetStatus($"{latestFolder.DisplayName}  {latestFolder.LastTime:HH:mm:ss}");
    }

    private FolderActivity? AddRecord(ChangeRecord record)
    {
        if (IsExcluded(record.FolderPath))
        {
            return null;
        }

        var folder = _activityHistory.Add(record);
        if (!_folders.ContainsKey(record.FolderPath))
        {
            _folders.Add(record.FolderPath, folder);
        }
        return folder;
    }

    private void ClearPendingRecords()
    {
        _pendingRecords.Clear();
    }

    private void TrimFolders()
    {
        if (_folders.Count <= MaxFolders)
        {
            return;
        }

        foreach (var stale in _folders.Values.OrderBy(item => item.LastTime).Take(_folders.Count - MaxFolders).ToArray())
        {
            _folders.Remove(stale.FolderPath);
            _expandedFolders.Remove(stale.FolderPath);
        }
    }

    private void RenderFolders()
    {
        if (ShouldSuspendMainRendering(_floatingModeActive, WindowState) || _folderList.IsDisposed)
        {
            return;
        }

        BeginControlUpdate(_folderList);
        _folderList.SuspendLayout();
        try
        {
            var width = Math.Max(1, _folderList.ClientSize.Width - 4);
            var orderedFolders = _folders.Values.OrderByDescending(item => item.LastTime).ToArray();
            var activePaths = orderedFolders.Select(folder => folder.FolderPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var stalePath in _folderCards.Keys.Where(path => !activePaths.Contains(path)).ToArray())
            {
                var staleCard = _folderCards[stalePath];
                _folderCards.Remove(stalePath);
                _folderList.Controls.Remove(staleCard);
                staleCard.Dispose();
            }

            for (var index = 0; index < orderedFolders.Length; index++)
            {
                var folder = orderedFolders[index];
                if (!_folderCards.TryGetValue(folder.FolderPath, out var card))
                {
                    card = new FolderCard(
                        folder,
                        expanded: _expandedFolders.Contains(folder.FolderPath),
                        isLatest: index == 0,
                        width,
                        OpenActivityFolder,
                        ToggleFolderExpanded,
                        OpenFile,
                        ExcludeFolder,
                        CopyFolderPath,
                        AddFavorite,
                        _favorites.Contains,
                        PinFolder,
                        _pinnedFolders.Contains,
                        RunFileCommand);
                    _folderCards.Add(folder.FolderPath, card);
                    _folderList.Controls.Add(card);
                }
                else
                {
                    card.UpdateActivity(
                        folder,
                        _expandedFolders.Contains(folder.FolderPath),
                        isLatest: index == 0,
                        width);
                }

                _folderList.Controls.SetChildIndex(card, index);
            }

            UpdateEmptyState();
        }
        finally
        {
            _folderList.ResumeLayout(performLayout: true);
            EndControlUpdate(_folderList);
        }
    }

    private void RefreshCardTimes()
    {
        if (ShouldSuspendMainRendering(_floatingModeActive, WindowState))
        {
            return;
        }

        if (_showFavorites)
        {
            foreach (var card in _favoriteCards.Values)
            {
                card.RefreshRelativeTime();
            }

            return;
        }

        foreach (var card in _folderCards.Values)
        {
            card.RefreshRelativeTime();
        }
    }

    private void DisposeFolderCards()
    {
        foreach (var card in _folderCards.Values)
        {
            _folderList.Controls.Remove(card);
            card.Dispose();
        }

        _folderCards.Clear();
    }

    private void RenderFavorites()
    {
        if (ShouldSuspendMainRendering(_floatingModeActive, WindowState)
            || !_showFavorites
            || _favoriteList.IsDisposed)
        {
            return;
        }

        BeginControlUpdate(_favoriteList);
        _favoriteList.SuspendLayout();
        try
        {
            var width = Math.Max(1, _favoriteList.ClientSize.Width - 4);
            var ordered = _favorites.OrderedItems;
            var activePaths = ordered.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var stalePath in _favoriteCards.Keys.Where(path => !activePaths.Contains(path)).ToArray())
            {
                var staleCard = _favoriteCards[stalePath];
                _favoriteCards.Remove(stalePath);
                _favoriteList.Controls.Remove(staleCard);
                staleCard.Dispose();
            }

            for (var index = 0; index < ordered.Count; index++)
            {
                var favorite = ordered[index];
                if (!_favoriteCards.TryGetValue(favorite.Path, out var card))
                {
                    card = new FavoriteFolderCard(
                        favorite,
                        isLatest: index == 0,
                        width,
                        OpenFolder,
                        RemoveFavorite,
                        CopyPath);
                    _favoriteCards.Add(favorite.Path, card);
                    _favoriteList.Controls.Add(card);
                }
                else
                {
                    card.UpdateFavorite(favorite, isLatest: index == 0, width);
                }

                _favoriteList.Controls.SetChildIndex(card, index);
            }

            UpdateEmptyState();
        }
        finally
        {
            _favoriteList.ResumeLayout(performLayout: true);
            EndControlUpdate(_favoriteList);
        }
    }

    private void DisposeFavoriteCards()
    {
        foreach (var card in _favoriteCards.Values)
        {
            _favoriteList.Controls.Remove(card);
            card.Dispose();
        }

        _favoriteCards.Clear();
    }

    private void SetFolderView(bool showFavorites)
    {
        _showFavorites = showFavorites;
        _folderList.Visible = !showFavorites;
        _favoriteHost.Visible = showFavorites;
        _clearButton.Visible = !showFavorites;
        ApplyTabStyle(_activeTabButton, !showFavorites);
        ApplyTabStyle(_favoriteTabButton, showFavorites);

        if (showFavorites)
        {
            _favoriteHost.BringToFront();
            RenderFavorites();
        }
        else
        {
            _folderList.BringToFront();
            RenderFolders();
        }

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var activeEmpty = _folders.Count == 0;
        var favoriteEmpty = _favorites.Count == 0;
        _emptyLabel.Text = "暂无活跃文件夹";
        _emptyLabel.Visible = !_showFavorites && activeEmpty;
        _favoriteEmptyLabel.Visible = _showFavorites && favoriteEmpty;
        if (_emptyLabel.Visible)
        {
            _emptyLabel.BringToFront();
        }

        if (_favoriteEmptyLabel.Visible)
        {
            _favoriteEmptyLabel.BringToFront();
        }
    }

    private void AddFavorite(FolderActivity folder)
    {
        AddFavoritePath(folder.FolderPath);
    }

    private void PinFolder(FolderActivity folder)
    {
        PinFolderPath(folder.FolderPath);
    }

    private void PinFolderPath(string path)
    {
        var displayName = GetFolderDisplayName(path);
        var position = SuggestedPinnedFolderPosition();
        if (!_pinnedFolders.Pin(path, position))
        {
            SetStatus($"已钉住 {displayName}");
            return;
        }

        SaveSettings();
        SyncPinnedFolderForms();
        SetStatus($"已钉住 {displayName}");
    }

    private void UnpinFolder(string path)
    {
        if (!_pinnedFolders.Remove(path))
        {
            return;
        }

        SaveSettings();
        SyncPinnedFolderForms();
        SetStatus($"已取消钉住 {GetFolderDisplayName(path)}");
    }

    private void SavePinnedFolderPosition(string path, Point location)
    {
        if (_pinnedFolders.UpdatePosition(path, location))
        {
            SaveSettings();
        }
    }

    private Point SuggestedPinnedFolderPosition()
    {
        var anchor = _floatingForm?.Visible == true
            ? _floatingForm.Location
            : Screen.PrimaryScreen?.WorkingArea.Location ?? Point.Empty;
        var index = _pinnedFolders.Count;
        return new Point(anchor.X - 108, anchor.Y - index * 104);
    }

    private void OpenAddFavoriteDialog()
    {
        using var dialog = new AddFavoriteForm();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        AddFavoritePath(dialog.SelectedPath);
    }

    private void AddFavoritePath(string path)
    {
        _folders.TryGetValue(path, out var folder);
        var displayName = folder?.DisplayName ?? GetFolderDisplayName(path);
        var lastActivity = folder?.LastTime ?? DateTime.Now;
        if (!_favorites.Add(path, lastActivity))
        {
            SetStatus($"已收藏 {displayName}");
            return;
        }

        _favoritesDirty = true;
        SaveSettings();
        UpdateFavoriteTabText();
        UpdateFloatingFavorites();
        if (_showFavorites)
        {
            RenderFavorites();
        }

        UpdateEmptyState();

        SetStatus($"已收藏 {displayName}");
    }

    private static string GetFolderDisplayName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private void RemoveFavorite(FavoriteFolder favorite)
    {
        if (!_favorites.Remove(favorite.Path))
        {
            return;
        }

        _favoritesDirty = true;
        SaveSettings();
        UpdateFavoriteTabText();
        UpdateFloatingFavorites();
        RenderFavorites();
        SetStatus($"已移除 {favorite.DisplayName}");
    }

    private void CopyPath(string path)
    {
        SetStatus(SafeClipboard.TrySetText(path, out var error)
            ? $"已复制：{path}"
            : $"复制失败：{error}");
    }

    private void UpdateFavoriteTabText()
    {
        _favoriteTabButton.Text = _favorites.Count == 0 ? "收藏" : $"收藏 ({_favorites.Count})";
    }

    private void MarkFavoritesDirty()
    {
        _favoritesDirty = true;
        if (!_favoriteSaveTimer.Enabled)
        {
            _favoriteSaveTimer.Start();
        }
    }

    private void PersistFavoriteActivity()
    {
        _favoriteSaveTimer.Stop();
        if (_favoritesDirty)
        {
            SaveSettings();
        }
    }

    private void ToggleFolderExpanded(FolderActivity folder)
    {
        var expanded = _expandedFolders.Add(folder.FolderPath);
        if (!expanded)
        {
            _expandedFolders.Remove(folder.FolderPath);
        }

        if (_floatingModeActive
            || !_folderCards.TryGetValue(folder.FolderPath, out var card)
            || _folderList.IsDisposed)
        {
            RenderFolders();
            return;
        }

        BeginControlUpdate(_folderList);
        _folderList.SuspendLayout();
        try
        {
            var width = Math.Max(1, _folderList.ClientSize.Width - 4);
            var isLatest = string.Equals(
                _latestFolder?.FolderPath,
                folder.FolderPath,
                StringComparison.OrdinalIgnoreCase);
            card.UpdateActivity(folder, expanded, isLatest, width);
        }
        finally
        {
            _folderList.ResumeLayout(performLayout: true);
            EndControlUpdate(_folderList);
        }
    }

    private void ExcludeFolder(FolderActivity folder)
    {
        AddExcludePath(folder.FolderPath);
        SaveSettings();
        ApplyExclusions();
        SetStatus($"已排除 {folder.DisplayName}");
    }

    private void CopyFolderPath(FolderActivity folder)
    {
        CopyPath(folder.FolderPath);
    }

    private async void RunFileCommand(ChangeRecord record, FileCommand command)
    {
        if (_isClosing)
        {
            return;
        }

        switch (command)
        {
            case FileCommand.Open:
                OpenFile(record);
                return;
            case FileCommand.CopyPath:
                CopyPath(record.FilePath);
                return;
            case FileCommand.CopyName:
                CopyPath(record.FileName);
                return;
            case FileCommand.CopyFile:
                if (!File.Exists(record.FilePath))
                {
                    SetStatus($"文件不存在或已被移动：{record.FileName}");
                    return;
                }

                SetStatus(SafeClipboard.TrySetFiles(new[] { record.FilePath }, out var copyError)
                    ? $"已复制文件：{record.FileName}，可直接粘贴"
                    : $"复制失败：{copyError}");
                return;
        }

        try
        {
            var done = await Task.Run(() => command == FileCommand.Reveal
                ? FileActions.TryRevealInExplorer(record.FilePath)
                : FileActions.TryOpenWith(record.FilePath));
            if (_isClosing || IsDisposed)
            {
                return;
            }

            if (!done)
            {
                SetStatus($"文件不存在或已被移动：{record.FileName}");
                if (command == FileCommand.Reveal)
                {
                    OpenFolder(record.FolderPath);
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or UnauthorizedAccessException
            or IOException)
        {
            if (!_isClosing && !IsDisposed)
            {
                SetStatus($"操作失败：{ex.Message}");
            }
        }
    }

    private async void OpenFile(ChangeRecord record)
    {
        if (_isClosing || string.IsNullOrWhiteSpace(record.FilePath))
        {
            return;
        }

        try
        {
            var opened = await Task.Run(() =>
            {
                if (!File.Exists(record.FilePath))
                {
                    return false;
                }

                Process.Start(new ProcessStartInfo(record.FilePath)
                {
                    UseShellExecute = true
                });
                return true;
            });

            if (!opened)
            {
                if (_isClosing || IsDisposed)
                {
                    return;
                }

                SetStatus($"文件不存在或已被移动：{record.FileName}");
                await RefreshLatestFileAsync(_latestFolder);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or UnauthorizedAccessException
            or IOException)
        {
            if (!_isClosing && !IsDisposed)
            {
                SetStatus($"打开文件失败：{ex.Message}");
            }
        }
    }

    private async void OpenFolder(string folderPath)
    {
        if (_isClosing || string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(folderPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            SetStatus($"无法打开文件夹：{ex.Message}");
            return;
        }

        if (!_openingFolderPaths.Add(normalizedPath))
        {
            return;
        }

        try
        {
            var error = await Task.Run(() =>
            {
                if (!Directory.Exists(normalizedPath))
                {
                    return $"文件夹不存在或当前不可访问：{normalizedPath}";
                }

                RunExplorer($"\"{normalizedPath}\"");
                return (string?)null;
            });

            if (!string.IsNullOrWhiteSpace(error))
            {
                SetStatus(error);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            SetStatus($"打开文件夹失败：{ex.Message}");
        }
        finally
        {
            _openingFolderPaths.Remove(normalizedPath);
        }
    }

    private void OpenLatestFolder()
    {
        if (_latestFolder is null)
        {
            SetStatus("暂无最新活跃文件夹");
            return;
        }

        OpenActivityFolder(_latestFolder.FolderPath);
    }

    /// <summary>
    /// 打开一个有活动记录的文件夹：开启“选中最新文件”时，在资源管理器中直接定位到最近写入的文件；
    /// 没有可用文件或关闭该选项时，退回普通打开。
    /// </summary>
    private async void OpenActivityFolder(string folderPath)
    {
        if (_isClosing || string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        var activity = FindActivity(folderPath);
        if (!_settings.SelectLatestFileWhenOpeningFolder || activity is null || activity.Files.Count == 0)
        {
            OpenFolder(folderPath);
            return;
        }

        if (!_openingFolderPaths.Add(folderPath))
        {
            return;
        }

        var records = activity.Files.ToArray();
        var revealed = false;
        try
        {
            revealed = await Task.Run(() => FileActions.TryRevealInExplorer(FileActions.LatestExistingFile(records)));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or UnauthorizedAccessException
            or IOException)
        {
            AppLog.Warning($"定位最新文件失败：{ex.Message}");
        }
        finally
        {
            _openingFolderPaths.Remove(folderPath);
        }

        if (!revealed && !_isClosing && !IsDisposed)
        {
            OpenFolder(folderPath);
        }
    }

    private FolderActivity? FindActivity(string folderPath)
    {
        if (_folders.TryGetValue(folderPath, out var folder))
        {
            return folder;
        }

        return _activityHistory.Items.FirstOrDefault(item =>
            string.Equals(item.FolderPath, folderPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
    }

    private void CopyLatestFolderPath()
    {
        if (_latestFolder is null)
        {
            SetStatus("暂无可复制的最新文件夹地址");
            return;
        }

        CopyPath(_latestFolder.FolderPath);
    }

    private void EnterFloatingMode()
    {
        if (_isClosing || _floatingModeActive || _floatingForm is null)
        {
            return;
        }

        CaptureMainWindowPlacement();
        _wasNativeMinimized = false;
        ShowInTaskbar = false;
        _floatingModeActive = true;
        Hide();
        _floatingForm.UpdateLatest(_latestFolder, _latestFile, _isMonitoring);
        UpdateFloatingFavorites();
        Point? savedLocation = _settings.FloatingLeft is int left && _settings.FloatingTop is int top
            ? new Point(left, top)
            : null;
        _floatingForm.ShowAt(savedLocation);
        SyncPinnedFolderForms();
    }

    private void RestoreMainWindow()
    {
        if (_isClosing)
        {
            return;
        }

        _restoreFloatingAfterTaskbarMinimize = false;
        var restoreBounds = _mainWindowRestoreBounds;
        var restoreState = _mainWindowRestoreState == FormWindowState.Minimized
            ? FormWindowState.Normal
            : _mainWindowRestoreState;
        _wasNativeMinimized = false;
        _floatingModeActive = false;
        _floatingForm?.Hide();
        HidePinnedFolderForms();
        ShowInTaskbar = true;
        Show();
        _ = ShowWindow(Handle, 9);
        WindowState = restoreState;
        if (restoreState == FormWindowState.Normal && IsUsableWindowBounds(restoreBounds))
        {
            Bounds = ClampWindowBounds(restoreBounds);
        }

        RenderCurrentMainView();
        BringToFront();
        Activate();
    }

    private void MinimizeMainWindow()
    {
        if (_isClosing)
        {
            return;
        }

        var restoreBounds = _mainWindowRestoreBounds;
        var restoreState = _mainWindowRestoreState == FormWindowState.Minimized
            ? FormWindowState.Normal
            : _mainWindowRestoreState;

        _floatingModeActive = false;
        _floatingForm?.Hide();
        HidePinnedFolderForms();

        ShowInTaskbar = true;
        WindowState = restoreState;
        if (restoreState == FormWindowState.Normal && IsUsableWindowBounds(restoreBounds))
        {
            Bounds = ClampWindowBounds(restoreBounds);
        }

        _restoreFloatingAfterTaskbarMinimize = true;
        _wasNativeMinimized = false;
        WindowState = FormWindowState.Minimized;
        Show();
    }

    private void RenderCurrentMainView()
    {
        if (_showFavorites)
        {
            RenderFavorites();
        }
        else
        {
            RenderFolders();
        }
    }

    internal static bool ShouldSuspendMainRendering(
        bool floatingModeActive,
        FormWindowState windowState)
    {
        return floatingModeActive || windowState == FormWindowState.Minimized;
    }

    internal static bool ShouldRestoreFloatingMode(
        bool restoreFloatingAfterTaskbarMinimize,
        int message,
        IntPtr command)
    {
        return restoreFloatingAfterTaskbarMinimize
            && message == WmSysCommand
            && (command.ToInt64() & SysCommandMask) == ScRestore;
    }

    private void CaptureMainWindowPlacement()
    {
        if (_floatingModeActive || _isClosing || !Visible || WindowState == FormWindowState.Minimized)
        {
            return;
        }

        _mainWindowRestoreState = WindowState;
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (IsUsableWindowBounds(bounds))
        {
            _mainWindowRestoreBounds = bounds;
        }
    }

    private static bool IsUsableWindowBounds(Rectangle bounds)
    {
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static Rectangle ClampWindowBounds(Rectangle bounds)
    {
        var screen = Screen.FromRectangle(bounds);
        var area = screen.WorkingArea;
        var width = Math.Min(bounds.Width, area.Width);
        var height = Math.Min(bounds.Height, area.Height);
        return new Rectangle(
            Math.Clamp(bounds.X, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(bounds.Y, area.Top, Math.Max(area.Top, area.Bottom - height)),
            width,
            height);
    }

    private void SaveFloatingPosition(Point location)
    {
        _settings.FloatingLeft = location.X;
        _settings.FloatingTop = location.Y;
        SaveSettings();
    }

    private void UpdateLatestFolder()
    {
        var previousPath = _latestFolder?.FolderPath;
        _latestFolder = _folders.Values.OrderByDescending(folder => folder.LastTime).FirstOrDefault();
        if (!string.Equals(previousPath, _latestFolder?.FolderPath, StringComparison.OrdinalIgnoreCase))
        {
            _latestFile = null;
        }

        _floatingForm?.UpdateLatest(_latestFolder, _latestFile, _isMonitoring);
        UpdateFloatingFavorites();
        _ = RefreshLatestFileAsync(_latestFolder);
    }

    private async Task RefreshLatestFileAsync(FolderActivity? folder)
    {
        var generation = ++_latestFileSelectionGeneration;
        if (folder is null)
        {
            _latestFile = null;
            _floatingForm?.UpdateLatest(null, null, _isMonitoring);
            return;
        }

        var folderPath = folder.FolderPath;
        var snapshot = folder.Files.ToArray();
        var selected = await Task.Run(() =>
            LatestFileSelector.SelectLatestExisting(snapshot, File.Exists));

        if (_isClosing
            || generation != _latestFileSelectionGeneration
            || !string.Equals(_latestFolder?.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _latestFile = selected;
        _floatingForm?.UpdateLatest(_latestFolder, _latestFile, _isMonitoring);
    }

    private void UpdateFloatingFavorites()
    {
        var count = AppSettings.NormalizeFloatingFavoriteCount(_settings.FloatingFavoriteCount);
        _floatingForm?.UpdateFavorites(_favorites.OrderedItems.Take(count).ToArray());
    }

    private void SyncPinnedFolderForms()
    {
        if (_isClosing)
        {
            return;
        }

        var items = _pinnedFolders.Items;
        var activePaths = items.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stalePath in _pinnedFolderForms.Keys.Where(path => !activePaths.Contains(path)).ToArray())
        {
            var staleForm = _pinnedFolderForms[stalePath];
            _pinnedFolderForms.Remove(stalePath);
            staleForm.Hide();
            staleForm.Dispose();
        }

        foreach (var item in items)
        {
            if (!_pinnedFolderForms.TryGetValue(item.Path, out var form))
            {
                form = new PinnedFolderForm(
                    item,
                    OpenPinnedFolder,
                    CopyPath,
                    AddFavoritePath,
                    UnpinFolder,
                    SavePinnedFolderPosition);
                _pinnedFolderForms.Add(item.Path, form);
            }
            else
            {
                form.UpdatePinnedFolder(item);
            }

            if (_floatingModeActive)
            {
                if (!form.Visible)
                {
                    form.ShowAt(item.Position);
                }
            }
            else
            {
                form.Hide();
            }
        }
    }

    private void HidePinnedFolderForms()
    {
        foreach (var form in _pinnedFolderForms.Values)
        {
            form.Hide();
        }
    }

    private void OpenPinnedFolder(string path)
    {
        if (_pinnedFolders.MarkOpened(path, DateTime.Now))
        {
            SaveSettings();
            SyncPinnedFolderForms();
        }

        OpenActivityFolder(path);
    }

    private void DisposePinnedFolderForms()
    {
        foreach (var form in _pinnedFolderForms.Values)
        {
            form.Dispose();
        }

        _pinnedFolderForms.Clear();
    }

    private void ApplyExclusions()
    {
        _monitor.SetExcludedPaths(_excludedPaths);

        foreach (var folder in _folders.Keys.Where(IsExcluded).ToArray())
        {
            _folders.Remove(folder);
            _expandedFolders.Remove(folder);
        }
        if (_activityHistory.RemoveWhere(IsExcluded) > 0)
        {
            MarkFavoritesDirty();
        }

        RenderFolders();
        UpdateLatestFolder();
    }

    private bool IsExcluded(string path)
    {
        return (_settings.FilterCommonNoise && PathRules.ContainsCommonNoiseDirectory(path))
            || PathRules.MatchesAny(path, _excludedPaths);
    }

    private void UpdateStatus()
    {
        var drives = _watchScopes.Where(scope => scope.Enabled).Select(scope => scope.Path.TrimEnd('\\')).ToArray();
        if (_toggleButton.Tag is Panel statusDot)
        {
            statusDot.BackColor = _isMonitoring ? Theme.Accent : Theme.Dim;
        }

        var statuses = _monitor.VolumeStatuses;
        var healthy = statuses.Count(status => status.State == VolumeMonitorState.Healthy);
        var attention = statuses.Count(status => status.State is VolumeMonitorState.Waiting or VolumeMonitorState.Reconnecting or VolumeMonitorState.Error);
        if (_isMonitoring)
        {
            var suffix = attention > 0 ? $" · {healthy} 正常 / {attention} 待恢复" : string.Empty;
            SetStatus(drives.Length == 0 ? $"监视中{suffix}" : $"监视中 · {string.Join(" ", drives)}{suffix}");
        }
        else
        {
            SetStatus(drives.Length == 0 ? "未选择硬盘" : $"已选择 · {string.Join(" ", drives)}");
        }

        _floatingForm?.UpdateLatest(_latestFolder, _latestFile, _isMonitoring);
        _trayIcon?.UpdateMonitoring(_isMonitoring, statuses);
        UpdateElevationBanner(statuses);
    }

    private void SaveSettings()
    {
        _settings.WatchScopes = _watchScopes
            .Select(scope => new SavedWatchScope
            {
                Path = scope.Path,
                Enabled = scope.Enabled
            })
            .ToList();
        _settings.ExcludedPaths = _excludedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _settings.FavoriteFolders = _favorites.ToSettings();
        _settings.PinnedFolders = _pinnedFolders.ToSettings();
        _settings.ActivityHistory = _activityHistory.ToSettings();
        try
        {
            _settings.Save();
            _favoritesDirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"设置保存失败：{ex.Message}");
            if (_favoritesDirty && !_isClosing && !_favoriteSaveTimer.Enabled)
            {
                _favoriteSaveTimer.Start();
            }
        }
    }

    private void ShowMonitorError(string message)
    {
        AppLog.Warning(message);
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed && !Disposing)
                {
                    SetStatus(message);
                }
            });
        }
        catch (InvalidOperationException)
        {
            // The window can lose its handle while a volume task is stopping.
        }
    }

    private void OnVolumeStatusChanged(VolumeMonitorStatus status)
    {
        AppLog.Info($"{status.VolumeRoot} {DiagnosticsSnapshot.StateText(status.State)}：{status.Message}");
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed && !Disposing)
                {
                    _isMonitoring = _monitor.IsRunning;
                    UpdateStatus();
                }
            });
        }
        catch (InvalidOperationException)
        {
            // The handle may be destroyed while a reconnect session exits.
        }
    }

    private void ShowDiagnostics()
    {
        using var dialog = new DiagnosticsForm(() => DiagnosticsSnapshot.Create(_monitor.VolumeStatuses));
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            dialog.ShowDialog(this);
        }
        else
        {
            dialog.ShowDialog();
        }
    }

    private void ShowActivityHistory()
    {
        using var dialog = new ActivityHistoryForm(() => _activityHistory.Items, OpenActivityFolder);
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            dialog.ShowDialog(this);
        }
        else
        {
            dialog.ShowDialog();
        }
    }

    private IReadOnlyList<string> RegisterConfiguredHotKeys()
    {
        _hotKeys?.Dispose();
        _hotKeys = new GlobalHotKeyManager(
            Handle,
            _settings.OpenLatestFolderHotKey,
            _settings.CopyLatestFolderPathHotKey,
            OpenLatestFolder,
            CopyLatestFolderPath);
        return _hotKeys.Register();
    }

    private static void RunExplorer(string arguments)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", arguments)
        {
            UseShellExecute = true
        });
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private static void BeginControlUpdate(Control control)
    {
        if (control.IsHandleCreated)
        {
            _ = SendMessage(control.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static void EndControlUpdate(Control control)
    {
        if (!control.IsHandleCreated)
        {
            return;
        }

        _ = SendMessage(control.Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
        control.Invalidate(invalidateChildren: true);
    }

    private void EnableDarkTitleBar()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var enabled = 1;
        if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
        }

        var captionColor = ColorTranslator.ToWin32(Color.Black);
        var borderColor = ColorTranslator.ToWin32(Theme.BorderStrong);
        var textColor = ColorTranslator.ToWin32(Color.White);
        _ = DwmSetWindowAttribute(Handle, 35, ref captionColor, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, 34, ref borderColor, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, 36, ref textColor, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
