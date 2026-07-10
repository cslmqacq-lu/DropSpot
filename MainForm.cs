using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DiskWriteWatcher;

public sealed class MainForm : Form
{
    private const int MaxFolders = 6;
    private const int MaxPendingRecords = 512;
    private const int MaxRecordsPerFlush = 128;
    private const string PlayIcon = "\uE768";
    private const string PauseIcon = "\uE769";
    private const string ClearIcon = "\uE74D";
    private const string SettingsIcon = "\uE713";

    private readonly FileMonitorService _monitor = new();
    private readonly Dictionary<string, FolderActivity> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FolderCard> _folderCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly FavoriteFolderStore _favorites = new();
    private readonly Dictionary<string, FavoriteFolderCard> _favoriteCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WatchScope> _watchScopes = new();
    private readonly List<string> _excludedPaths = new();
    private readonly PendingRecordBuffer _pendingRecords = new(MaxPendingRecords);
    private readonly System.Windows.Forms.Timer _uiFlushTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _favoriteSaveTimer = new() { Interval = 30000 };
    private readonly AppSettings _settings;
    private readonly ToolTip _toolTip = new();

    private readonly FlickerFreeFlowLayoutPanel _folderList = new();
    private readonly FlickerFreeFlowLayoutPanel _favoriteList = new();
    private readonly Label _statusLabel = new();
    private readonly Label _emptyLabel = new();
    private readonly Button _toggleButton = new();
    private readonly Button _clearButton = new();
    private readonly Button _activeTabButton = new();
    private readonly Button _favoriteTabButton = new();
    private readonly string? _settingsWarning;
    private FloatingFolderForm? _floatingForm;
    private FolderActivity? _latestFolder;
    private bool _isMonitoring;
    private bool _isClosing;
    private bool _showFavorites;
    private bool _favoritesDirty;
    private bool _floatingModeActive;
    private Rectangle _mainWindowRestoreBounds;
    private FormWindowState _mainWindowRestoreState = FormWindowState.Normal;

    public MainForm()
    {
        Text = $"活跃文件夹 v{Application.ProductVersion}";
        MinimumSize = new Size(460, 500);
        Size = new Size(500, 610);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildUi();
        _settings = AppSettings.Load(out _settingsWarning);
        _floatingForm = new FloatingFolderForm(
            OpenLatestFolder,
            OpenFolder,
            RestoreMainWindow,
            ToggleMonitor,
            Close,
            CopyPath,
            AddFavoritePath,
            SaveFloatingPosition,
            () => _isMonitoring);
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
        SizeChanged += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && !_isClosing)
            {
                if (IsUsableWindowBounds(RestoreBounds))
                {
                    _mainWindowRestoreBounds = RestoreBounds;
                }

                BeginInvoke(ShowFloatingMode);
                return;
            }

            CaptureMainWindowPlacement();
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
            _monitor.Dispose();
            _floatingForm?.Close();
            _floatingForm?.Dispose();
            _floatingForm = null;
            DisposeFolderCards();
            DisposeFavoriteCards();
            ShellIconProvider.DisposeCache();
        };
        Shown += (_, _) =>
        {
            CaptureMainWindowPlacement();
            if (_watchScopes.Any(scope => scope.Enabled))
            {
                BeginInvoke(() =>
                {
                    StartMonitor(promptIfMissing: false);
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
        };
        UpdateStatus();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        EnableDarkTitleBar();
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

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

        void LayoutHeader()
        {
            const int actionWidth = 118;
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
        root.Controls.Add(tabHost, 0, 1);

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
        root.Controls.Add(listHost, 0, 2);

        _folderList.Dock = DockStyle.Fill;
        _folderList.FlowDirection = FlowDirection.TopDown;
        _folderList.WrapContents = false;
        _folderList.AutoScroll = true;
        _folderList.BackColor = Theme.Window;
        _folderList.Resize += (_, _) => RenderFolders();
        listHost.Controls.Add(_folderList);

        _favoriteList.Dock = DockStyle.Fill;
        _favoriteList.FlowDirection = FlowDirection.TopDown;
        _favoriteList.WrapContents = false;
        _favoriteList.AutoScroll = true;
        _favoriteList.BackColor = Theme.Window;
        _favoriteList.Visible = false;
        _favoriteList.Resize += (_, _) =>
        {
            if (_showFavorites)
            {
                RenderFavorites();
            }
        };
        listHost.Controls.Add(_favoriteList);

        _emptyLabel.Text = "暂无活跃文件夹";
        _emptyLabel.Dock = DockStyle.Fill;
        _emptyLabel.ForeColor = Theme.Muted;
        _emptyLabel.BackColor = Theme.Window;
        _emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
        _emptyLabel.Font = new Font("Microsoft YaHei UI", 10F);
        listHost.Controls.Add(_emptyLabel);
        _emptyLabel.BringToFront();
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

        _favorites.Load(_settings.FavoriteFolders);
        UpdateFavoriteTabText();
        UpdateFloatingFavorites();
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
        using var dialog = new SettingsForm(_watchScopes, _excludedPaths);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _watchScopes.Clear();
        _watchScopes.AddRange(dialog.WatchScopes.Select(scope => new WatchScope(scope.Path, scope.Enabled)));

        _excludedPaths.Clear();
        _excludedPaths.AddRange(dialog.ExcludedPaths);
        SaveSettings();

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

    private void StartMonitor(bool promptIfMissing)
    {
        var scopes = _watchScopes.Where(scope => scope.Enabled).ToArray();
        if (scopes.Length == 0)
        {
            SetStatus("请先选择硬盘");
            if (promptIfMissing)
            {
                OpenSettings();
            }
            return;
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
        _expandedFolders.Clear();
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
        foreach (var record in records)
        {
            var folder = AddRecord(record);
            latestFolder = folder ?? latestFolder;
            if (folder is not null && _favorites.MarkActivity(record.FolderPath, record.Time))
            {
                favoriteActivityChanged = true;
            }
        }

        if (latestFolder is null)
        {
            return;
        }

        TrimFolders();
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
        UpdateLatestFolder();
        SetStatus($"{latestFolder.DisplayName}  {latestFolder.LastTime:HH:mm:ss}");
    }

    private FolderActivity? AddRecord(ChangeRecord record)
    {
        if (IsExcluded(record.FolderPath))
        {
            return null;
        }

        if (!_folders.TryGetValue(record.FolderPath, out var folder))
        {
            folder = new FolderActivity(record.FolderPath);
            _folders.Add(record.FolderPath, folder);
        }

        folder.Add(record);
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
        if (_floatingModeActive || _folderList.IsDisposed)
        {
            return;
        }

        BeginControlUpdate(_folderList);
        _folderList.SuspendLayout();
        try
        {
            var width = Math.Max(320, _folderList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
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
                        OpenFolder,
                        ToggleFolderExpanded,
                        OpenFile,
                        ExcludeFolder,
                        CopyFolderPath,
                        AddFavorite,
                        _favorites.Contains);
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
        if (_floatingModeActive)
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
        if (_floatingModeActive || !_showFavorites || _favoriteList.IsDisposed)
        {
            return;
        }

        BeginControlUpdate(_favoriteList);
        _favoriteList.SuspendLayout();
        try
        {
            var width = Math.Max(320, _favoriteList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
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
        _favoriteList.Visible = showFavorites;
        _clearButton.Visible = !showFavorites;
        ApplyTabStyle(_activeTabButton, !showFavorites);
        ApplyTabStyle(_favoriteTabButton, showFavorites);

        if (showFavorites)
        {
            _favoriteList.BringToFront();
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
        var empty = _showFavorites ? _favorites.Count == 0 : _folders.Count == 0;
        _emptyLabel.Text = _showFavorites ? "暂无收藏文件夹" : "暂无活跃文件夹";
        _emptyLabel.Visible = empty;
        if (empty)
        {
            _emptyLabel.BringToFront();
        }
    }

    private void AddFavorite(FolderActivity folder)
    {
        AddFavoritePath(folder.FolderPath);
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

    private static void CopyPath(string path)
    {
        Clipboard.SetText(path);
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
        if (!_expandedFolders.Add(folder.FolderPath))
        {
            _expandedFolders.Remove(folder.FolderPath);
        }

        RenderFolders();
    }

    private void ExcludeFolder(FolderActivity folder)
    {
        AddExcludePath(folder.FolderPath);
        SaveSettings();
        ApplyExclusions();
        SetStatus($"已排除 {folder.DisplayName}");
    }

    private static void CopyFolderPath(FolderActivity folder)
    {
        Clipboard.SetText(folder.FolderPath);
    }

    private void OpenFile(ChangeRecord record)
    {
        if (File.Exists(record.FilePath))
        {
            Process.Start(new ProcessStartInfo(record.FilePath)
            {
                UseShellExecute = true
            });
            return;
        }

        OpenFolder(record.FolderPath);
    }

    private void OpenFolder(string folderPath)
    {
        if (Directory.Exists(folderPath))
        {
            RunExplorer($"\"{folderPath}\"");
            return;
        }

        SetStatus($"文件夹不存在：{folderPath}");
    }

    private void OpenLatestFolder()
    {
        if (_latestFolder is not null && Directory.Exists(_latestFolder.FolderPath))
        {
            OpenFolder(_latestFolder.FolderPath);
        }
    }

    private void ShowFloatingMode()
    {
        if (_isClosing || WindowState != FormWindowState.Minimized || _floatingForm is null)
        {
            return;
        }

        ShowInTaskbar = false;
        _floatingModeActive = true;
        Hide();
        _floatingForm.UpdateLatest(_latestFolder, _isMonitoring);
        UpdateFloatingFavorites();
        Point? savedLocation = _settings.FloatingLeft is int left && _settings.FloatingTop is int top
            ? new Point(left, top)
            : null;
        _floatingForm.ShowAt(savedLocation);
    }

    private void RestoreMainWindow()
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
        _latestFolder = _folders.Values.OrderByDescending(folder => folder.LastTime).FirstOrDefault();
        _floatingForm?.UpdateLatest(_latestFolder, _isMonitoring);
        UpdateFloatingFavorites();
    }

    private void UpdateFloatingFavorites()
    {
        _floatingForm?.UpdateFavorites(_favorites.OrderedItems.Take(5).ToArray());
    }

    private void ApplyExclusions()
    {
        _monitor.SetExcludedPaths(_excludedPaths);

        foreach (var folder in _folders.Keys.Where(IsExcluded).ToArray())
        {
            _folders.Remove(folder);
            _expandedFolders.Remove(folder);
        }

        RenderFolders();
        UpdateLatestFolder();
    }

    private bool IsExcluded(string path)
    {
        return _excludedPaths.Any(excluded => IsPathUnder(path, excluded));
    }

    private static bool IsPathUnder(string path, string excluded)
    {
        if (string.IsNullOrWhiteSpace(excluded))
        {
            return false;
        }

        if (excluded is "$Recycle.Bin" or "System Volume Information")
        {
            return path.Contains("\\" + excluded + "\\", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullExcluded = Path.GetFullPath(excluded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullPath, fullExcluded, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(fullExcluded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void UpdateStatus()
    {
        var drives = _watchScopes.Where(scope => scope.Enabled).Select(scope => scope.Path.TrimEnd('\\')).ToArray();
        if (_toggleButton.Tag is Panel statusDot)
        {
            statusDot.BackColor = _isMonitoring ? Theme.Accent : Theme.Dim;
        }

        if (_isMonitoring)
        {
            SetStatus(drives.Length == 0 ? "监视中" : $"监视中 · {string.Join(" ", drives)}");
        }
        else
        {
            SetStatus(drives.Length == 0 ? "未选择硬盘" : $"已选择 · {string.Join(" ", drives)}");
        }

        _floatingForm?.UpdateLatest(_latestFolder, _isMonitoring);
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
        control.Update();
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
