using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DropSpot;

public static class SmokeTest
{
    public static int RunUiOnly()
    {
        try
        {
            return RunUiResourceTests();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 90;
        }
    }

    public static int Run()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "DropSpotSmoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            Console.WriteLine("[smoke] monitor");
            var monitorResult = RunMonitorTests(tempRoot);
            if (monitorResult != 0)
            {
                return monitorResult;
            }

            Console.WriteLine("[smoke] buffer");
            var bufferResult = RunBufferTests();
            if (bufferResult != 0)
            {
                return bufferResult;
            }

            Console.WriteLine("[smoke] reliability-state");
            var reliabilityResult = RunReliabilityStateTests();
            if (reliabilityResult != 0)
            {
                return reliabilityResult;
            }

            Console.WriteLine("[smoke] path-rules");
            var pathRulesResult = RunPathRuleTests();
            if (pathRulesResult != 0)
            {
                return pathRulesResult;
            }

            Console.WriteLine("[smoke] latest-file-selector");
            var latestFileResult = RunLatestFileSelectorTests();
            if (latestFileResult != 0)
            {
                return latestFileResult;
            }

            Console.WriteLine("[smoke] single-instance");
            var singleInstanceResult = RunSingleInstanceTests();
            if (singleInstanceResult != 0)
            {
                return singleInstanceResult;
            }

            Console.WriteLine("[smoke] placement");
            var placementResult = RunPlacementTests();
            if (placementResult != 0)
            {
                return placementResult;
            }

            Console.WriteLine("[smoke] favorites");
            var favoriteResult = RunFavoriteTests();
            if (favoriteResult != 0)
            {
                return favoriteResult;
            }

            Console.WriteLine("[smoke] pinned-folders");
            var pinnedResult = RunPinnedFolderTests();
            if (pinnedResult != 0)
            {
                return pinnedResult;
            }

            Console.WriteLine("[smoke] window-presentation");
            var windowPresentationResult = RunWindowPresentationTests();
            if (windowPresentationResult != 0)
            {
                return windowPresentationResult;
            }

            Console.WriteLine("[smoke] ui-resources");
            var uiResult = RunUiResourceTests();
            if (uiResult != 0)
            {
                return uiResult;
            }

            Console.WriteLine("[smoke] settings");
            return RunSettingsTests(tempRoot);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 90;
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Smoke-test cleanup should not mask the monitor result.
            }
        }
    }

    private static int RunMonitorTests(string tempRoot)
    {
        using var monitor = new FileMonitorService();
        var records = new ConcurrentBag<ChangeRecord>();
        monitor.Changed += (_, record) => records.Add(record);
        var missingRoot = Enumerable.Range('D', 'Z' - 'D' + 1)
            .Select(letter => $"{(char)letter}:\\")
            .FirstOrDefault(root => !Directory.Exists(root));
        var scopes = missingRoot is null
            ? new[] { new WatchScope(tempRoot) }
            : new[] { new WatchScope(tempRoot), new WatchScope(Path.Combine(missingRoot, "DropSpotMissing")) };
        monitor.Start(scopes);
        if (!SpinWait.SpinUntil(
                () => monitor.VolumeStatuses.Any(status => status.State == VolumeMonitorState.Healthy),
                TimeSpan.FromSeconds(8)))
        {
            return 7;
        }
        if (missingRoot is not null
            && !SpinWait.SpinUntil(
                () => monitor.VolumeStatuses.Count == 2
                    && monitor.VolumeStatuses.Any(status => status.VolumeRoot == missingRoot && status.State != VolumeMonitorState.Healthy),
                TimeSpan.FromSeconds(4)))
        {
            return 9;
        }

        var filePath = Path.Combine(tempRoot, "probe.txt");
        File.WriteAllText(filePath, "created");
        if (!WaitForRecord(records, filePath, minimumCount: 1, TimeSpan.FromSeconds(5)))
        {
            return 1;
        }

        Thread.Sleep(1700);
        var countBeforeUpdate = CountFor(records, filePath);
        File.AppendAllText(filePath, Environment.NewLine + "updated");
        if (!WaitForRecord(records, filePath, countBeforeUpdate + 1, TimeSpan.FromSeconds(5)))
        {
            return 2;
        }

        var countBeforeDelete = CountFor(records, filePath);
        File.Delete(filePath);
        Thread.Sleep(1200);
        if (CountFor(records, filePath) != countBeforeDelete)
        {
            return 3;
        }

        var excludedRoot = Path.Combine(tempRoot, "excluded");
        Directory.CreateDirectory(excludedRoot);
        monitor.SetExcludedPaths(new[] { excludedRoot });
        var excludedFile = Path.Combine(excludedRoot, "ignored.txt");
        File.WriteAllText(excludedFile, "ignored");
        Thread.Sleep(1200);
        if (CountFor(records, excludedFile) != 0)
        {
            return 4;
        }

        monitor.Stop();
        var stoppedFile = Path.Combine(tempRoot, "while-stopped.txt");
        File.WriteAllText(stoppedFile, "offline");
        monitor.Start(new[] { new WatchScope(tempRoot) });
        if (!SpinWait.SpinUntil(
                () => monitor.VolumeStatuses.Any(status => status.State == VolumeMonitorState.Healthy),
                TimeSpan.FromSeconds(8)))
        {
            return 8;
        }
        Thread.Sleep(500);
        if (CountFor(records, stoppedFile) != 0)
        {
            return 5;
        }

        var restartedFile = Path.Combine(tempRoot, "after-restart.txt");
        File.WriteAllText(restartedFile, "online");
        return WaitForRecord(records, restartedFile, minimumCount: 1, TimeSpan.FromSeconds(5)) ? 0 : 6;
    }

    private static int RunSingleInstanceTests()
    {
        var key = "DropSpot.Smoke." + Guid.NewGuid().ToString("N");
        using var activated = new ManualResetEventSlim(false);
        using var first = new SingleInstanceCoordinator(key);
        using var second = new SingleInstanceCoordinator(key);
        if (!first.IsFirstInstance || second.IsFirstInstance)
        {
            return 60;
        }

        first.StartListening(activated.Set);
        second.SignalActivation();
        if (!activated.Wait(TimeSpan.FromSeconds(2)))
        {
            return 61;
        }

        var command = StartupRegistration.BuildCommand(@"C:\Program Files\DropSpot\DropSpot.exe");
        if (command != "\"C:\\Program Files\\DropSpot\\DropSpot.exe\" --startup")
        {
            return 62;
        }

        var root = Path.Combine(Path.GetTempPath(), "DropSpotStartupSmoke-" + Guid.NewGuid().ToString("N"));
        var stablePath = Path.Combine(root, "portable", "DropSpot.exe");
        var installedPath = Path.Combine(root, "installed", "DropSpot.exe");
        var developmentPath = Path.Combine(root, "repo", "bin", "Debug", "net8.0-windows", "DropSpot.exe");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stablePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(installedPath)!);
            File.WriteAllText(stablePath, string.Empty);
            File.WriteAllText(installedPath, string.Empty);

            var preserved = StartupRegistration.ResolveStartupExecutable(
                developmentPath,
                StartupRegistration.BuildCommand(stablePath),
                installedPath);
            if (!StartupRegistration.IsDevelopmentExecutable(developmentPath)
                || !string.Equals(preserved, stablePath, StringComparison.OrdinalIgnoreCase))
            {
                return 63;
            }

            File.Delete(stablePath);
            var fallback = StartupRegistration.ResolveStartupExecutable(
                developmentPath,
                StartupRegistration.BuildCommand(stablePath),
                installedPath);
            return string.Equals(fallback, installedPath, StringComparison.OrdinalIgnoreCase) ? 0 : 64;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static int RunPathRuleTests()
    {
        // 名称规则：任意层级目录名匹配
        if (!PathRules.Matches(@"D:\code\app\node_modules\lodash", "node_modules")
            || PathRules.Matches(@"D:\code\app\my_node_modules_backup", "node_modules"))
        {
            return 150;
        }

        // 通配符规则
        if (!PathRules.Matches(@"D:\video\._jianying_export_temp_folder_178\a.mp4", "*_temp_folder_*")
            || !PathRules.Matches(@"D:\logs\app.log", "*.log"))
        {
            return 151;
        }

        // 完整路径规则：只排除自身与子目录，不误伤同名前缀
        if (!PathRules.Matches(@"G:\Temp\sub", @"G:\Temp")
            || !PathRules.Matches(@"G:\Temp", @"G:\Temp\")
            || PathRules.Matches(@"G:\Temporary", @"G:\Temp"))
        {
            return 152;
        }

        // 旧版默认规则 $Recycle.Bin 仍然有效
        if (!PathRules.Matches(@"C:\$Recycle.Bin\S-1-5\x", "$Recycle.Bin"))
        {
            return 153;
        }

        if (!PathRules.ContainsCommonNoiseDirectory(@"G:\repo\.git\objects")
            || PathRules.ContainsCommonNoiseDirectory(@"G:\repo\src"))
        {
            return 154;
        }

        if (!PathRules.IsTemporaryFileName("~$报告.docx")
            || !PathRules.IsTemporaryFileName("setup.crdownload")
            || !PathRules.IsTemporaryFileName("Thumbs.db")
            || PathRules.IsTemporaryFileName("报告.docx"))
        {
            return 155;
        }

        var now = new DateTime(2026, 10, 1, 12, 0, 0);
        if (TimeText.Relative(now.AddSeconds(-10), now) != "刚刚"
            || TimeText.Relative(now.AddMinutes(-5), now) != "5 分钟"
            || TimeText.Relative(now.AddHours(-3), now) != "09:00"
            || TimeText.Clock(now.AddDays(-1), now) != "昨天 12:00"
            || TimeText.Clock(now.AddDays(-3), now) != "09-28 12:00"
            || TimeText.Clock(now.AddYears(-1), now) != "2025-10-01")
        {
            return 156;
        }

        var xml = ScheduledTasks.BuildElevatedTaskXml(
            @"C:\Program Files\DropSpot\DropSpot.exe",
            @"PC\user&co",
            MonitorAgent.AgentArgument,
            "DropSpot 后台监视",
            runAtLogon: false);
        var logonXml = ScheduledTasks.BuildElevatedTaskXml(@"C:\x\DropSpot.exe", "PC\\u", "--startup", "d", runAtLogon: true);
        if (!xml.Contains("<RunLevel>HighestAvailable</RunLevel>")
            || !xml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>")
            || !xml.Contains("user&amp;co")
            || !xml.Contains("<Arguments>--monitor-agent</Arguments>")
            || xml.Contains("<LogonTrigger>")
            || !logonXml.Contains("<LogonTrigger>")
            || !xml.StartsWith("<?xml", StringComparison.Ordinal)
            || !System.Xml.Linq.XDocument.Parse(xml).Root!.Name.LocalName.Equals("Task"))
        {
            return 157;
        }

        // 收藏文件：归到最深的已收藏上级文件夹；没有时自动收藏父文件夹；可保存 / 恢复
        var store = new FavoriteFolderStore();
        store.Add(@"C:\work", now);
        store.Add(@"C:\work\sub", now);
        var nested = store.AddFile(@"C:\work\sub\deep\a.txt", now);
        var created = store.AddFile(@"D:\other\b.txt", now);
        var duplicate = store.AddFile(@"D:\other\b.txt", now);
        if (nested.Folder?.Path != @"C:\work\sub" || nested.FolderCreated
            || created.Folder?.Path != @"D:\other" || !created.FolderCreated
            || !duplicate.AlreadyFavorite
            || store.FileCount != 2
            || store.Count != 3)
        {
            return 158;
        }

        var restored = new FavoriteFolderStore();
        restored.Load(store.ToSettings());
        if (restored.FileCount != 2 || !restored.ContainsFile(@"C:\work\sub\deep\a.txt"))
        {
            return 159;
        }

        if (!restored.RemoveFile(@"D:\other\b.txt") || restored.FileCount != 1 || restored.AllFiles[0].RelativeName != @"deep\a.txt")
        {
            return 160;
        }

        // 后台监视进程与界面之间的消息可以完整往返
        var sentRecord = new ChangeRecord
        {
            Time = now,
            ChangeKind = "新建",
            FolderPath = @"G:\下载",
            FilePath = @"G:\下载\报告.docx",
            ScopePath = @"G:\",
            FileName = "报告.docx"
        };
        var sentStatus = VolumeMonitorStatus.Waiting(@"G:\") with { State = VolumeMonitorState.Error, AccessDenied = true, Message = "x" };
        var receivedRecord = System.Text.Json.JsonSerializer.Deserialize<AgentMessage>(
            System.Text.Json.JsonSerializer.Serialize(AgentMessage.FromRecord(sentRecord)))?.ToRecord();
        var receivedStatus = System.Text.Json.JsonSerializer.Deserialize<AgentMessage>(
            System.Text.Json.JsonSerializer.Serialize(AgentMessage.FromStatus(sentStatus)))?.ToStatus();
        if (receivedRecord is null
            || receivedRecord.FilePath != sentRecord.FilePath
            || receivedRecord.Time != sentRecord.Time
            || receivedRecord.ChangeKind != "新建"
            || receivedStatus is null
            || receivedStatus.State != VolumeMonitorState.Error
            || !receivedStatus.AccessDenied
            || !MonitorAgent.PipeName.StartsWith("DropSpot.Monitor.", StringComparison.Ordinal))
        {
            return 161;
        }

        return 0;
    }

    private static int RunBufferTests()
    {
        var start = DateTime.UtcNow;
        var buffer = new PendingRecordBuffer(capacity: 10);
        for (var index = 0; index < 100; index++)
        {
            buffer.Add(CreateRecord($@"C:\stress\file-{index}.txt", start.AddMilliseconds(index)));
        }

        if (buffer.Count != 10)
        {
            return 20;
        }

        var latest = buffer.DrainLatest(maxCount: 4);
        if (latest.Count != 4
            || latest[^1].FileName != "file-99.txt"
            || buffer.Count != 0)
        {
            return 21;
        }

        var samePath = @"C:\stress\same.txt";
        buffer.Add(CreateRecord(samePath, start));
        buffer.Add(CreateRecord(samePath, start.AddSeconds(1)));
        var merged = buffer.DrainLatest(maxCount: 10);
        return merged.Count == 1 && merged[0].Time == start.AddSeconds(1) ? 0 : 22;
    }

    private static int RunLatestFileSelectorTests()
    {
        var now = DateTime.UtcNow;
        var records = new[]
        {
            CreateRecord(@"C:\work\render.tmp", now.AddSeconds(4)),
            CreateRecord(@"C:\work\download.part", now.AddSeconds(3)),
            CreateRecord(@"C:\work\~$brief.docx", now.AddSeconds(2)),
            CreateRecord(@"C:\work\final.png", now.AddSeconds(1)),
            CreateRecord(@"C:\work\older.jpg", now)
        };
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\work\final.png",
            @"C:\work\older.jpg"
        };

        var selected = LatestFileSelector.SelectLatestExisting(records, existing.Contains);
        if (selected?.FilePath != @"C:\work\final.png")
        {
            return 71;
        }

        existing.Remove(@"C:\work\final.png");
        selected = LatestFileSelector.SelectLatestExisting(records, existing.Contains);
        if (selected?.FilePath != @"C:\work\older.jpg")
        {
            return 72;
        }

        existing.Clear();
        return LatestFileSelector.SelectLatestExisting(records, existing.Contains) is null ? 0 : 73;
    }

    private static int RunSettingsTests(string tempRoot)
    {
        var settingsPath = Path.Combine(tempRoot, "settings.json");
        var original = new AppSettings
        {
            WatchScopes = new List<SavedWatchScope>
            {
                new() { Path = @"C:\", Enabled = true }
            },
            ExcludedPaths = new List<string> { @"C:\Temp" },
            FavoriteFolders = new List<SavedFavoriteFolder>
            {
                new()
                {
                    Path = @"C:\Projects",
                    AddedAt = new DateTime(2026, 1, 1),
                    LastActivity = new DateTime(2026, 2, 1)
                }
            },
            PinnedFolders = new List<SavedPinnedFolder>
            {
                new()
                {
                    Path = @"C:\Projects\Inbox",
                    Left = 180,
                    Top = 240,
                    PinnedAt = new DateTime(2026, 3, 1),
                    LastActivity = new DateTime(2026, 3, 2),
                    LastOpenedAt = new DateTime(2026, 3, 1, 12, 0, 0)
                }
            },
            ActivityHistory = new List<SavedActivityFolder>
            {
                new()
                {
                    FolderPath = @"C:\Projects\Recent",
                    LastTime = new DateTime(2026, 3, 3),
                    ChangeCount = 4,
                    Files = new List<SavedActivityFile>
                    {
                        new()
                        {
                            Time = new DateTime(2026, 3, 3),
                            ChangeKind = "更新",
                            FilePath = @"C:\Projects\Recent\image.png",
                            ScopePath = @"C:\",
                            FileName = "image.png"
                        }
                    }
                }
            },
            FloatingLeft = 123,
            FloatingTop = 456,
            FloatingFavoriteCount = 8,
            StartWithWindows = true
        };
        original.SaveTo(settingsPath);

        var updated = new AppSettings
        {
            WatchScopes = new List<SavedWatchScope>
            {
                new() { Path = @"D:\", Enabled = true }
            }
        };
        updated.SaveTo(settingsPath);

        File.WriteAllText(settingsPath, "{ damaged json");
        var recovered = AppSettings.LoadFrom(settingsPath, out var warning);
        if (warning is null
            || recovered.WatchScopes.Count != 1
            || recovered.WatchScopes[0].Path != @"C:\"
            || recovered.FavoriteFolders.Count != 1
            || recovered.FavoriteFolders[0].Path != @"C:\Projects"
            || recovered.PinnedFolders.Count != 1
            || recovered.PinnedFolders[0].Path != @"C:\Projects\Inbox"
            || recovered.PinnedFolders[0].Left != 180
            || recovered.PinnedFolders[0].Top != 240
            || recovered.PinnedFolders[0].LastActivity != new DateTime(2026, 3, 2)
            || recovered.ActivityHistory.Count != 1
            || recovered.ActivityHistory[0].Files.Count != 1
            || recovered.OpenLatestFolderHotKey.DisplayText() != "Ctrl+Alt+F"
            || recovered.CopyLatestFolderPathHotKey.DisplayText() != "Ctrl+Alt+D"
            || recovered.FloatingLeft != 123
            || recovered.FloatingTop != 456
            || recovered.FloatingFavoriteCount != 8
            || !recovered.StartWithWindows
            || AppSettings.NormalizeFloatingFavoriteCount(0) != 1
            || AppSettings.NormalizeFloatingFavoriteCount(99) != 14)
        {
            return 30;
        }

        File.Delete(settingsPath);
        recovered = AppSettings.LoadFrom(settingsPath, out warning);
        if (warning is null || recovered.WatchScopes.Count != 1)
        {
            return 31;
        }

        var legacyPath = Path.Combine(tempRoot, "legacy", "settings.json");
        var currentPath = Path.Combine(tempRoot, "current", "settings.json");
        original.SaveTo(legacyPath);
        if (!AppSettings.MigrateLegacySettings(legacyPath, currentPath, out var migrationWarning)
            || string.IsNullOrWhiteSpace(migrationWarning)
            || !File.Exists(currentPath))
        {
            return 32;
        }

        var migrated = AppSettings.LoadFrom(currentPath, out warning);
        if (warning is not null
            || migrated.FavoriteFolders.Count != 1
            || migrated.FavoriteFolders[0].Path != @"C:\Projects"
            || migrated.PinnedFolders.Count != 1
            || migrated.FloatingFavoriteCount != 8)
        {
            return 33;
        }

        new AppSettings
        {
            WatchScopes = new List<SavedWatchScope>
            {
                new() { Path = @"Z:\", Enabled = true }
            }
        }.SaveTo(legacyPath);

        if (AppSettings.MigrateLegacySettings(legacyPath, currentPath, out migrationWarning))
        {
            return 34;
        }

        migrated = AppSettings.LoadFrom(currentPath, out warning);
        if (warning is not null
            || migrated.WatchScopes.Count != 1
            || migrated.WatchScopes[0].Path != @"C:\")
        {
            return 35;
        }

        var legacyJsonPath = Path.Combine(tempRoot, "legacy-hotkeys.json");
        File.WriteAllText(legacyJsonPath, "{\"WatchScopes\":[]}");
        var legacyDefaults = AppSettings.LoadFrom(legacyJsonPath, out warning);
        return warning is null
            && legacyDefaults.OpenLatestFolderHotKey.DisplayText() == "Ctrl+Alt+F"
            && legacyDefaults.CopyLatestFolderPathHotKey.DisplayText() == "Ctrl+Alt+D"
            ? 0
            : 36;
    }

    private static int RunReliabilityStateTests()
    {
        if (UsnJournalVolumeWatcher.RetryDelay(1) != TimeSpan.FromSeconds(2)
            || UsnJournalVolumeWatcher.RetryDelay(4) != TimeSpan.FromSeconds(8)
            || UsnJournalVolumeWatcher.RetryDelay(99) != TimeSpan.FromSeconds(30))
        {
            return 81;
        }

        var status = new VolumeMonitorStatus(
            @"G:\",
            VolumeMonitorState.Reconnecting,
            "磁盘尚未就绪",
            3,
            new DateTime(2026, 8, 29, 10, 0, 0),
            new DateTime(2026, 8, 29, 10, 1, 0),
            new DateTime(2026, 8, 29, 10, 2, 0));
        var diagnostic = new DiagnosticsSnapshot(
            "1.0.7",
            new DateTime(2026, 8, 29, 10, 3, 0),
            new[] { status },
            new[] { "sample log" },
            @"C:\logs").Format();
        var openBinding = SavedHotKey.OpenLatestFolderDefault();
        var copyBinding = SavedHotKey.CopyLatestFolderPathDefault();
        var invalidBinding = new SavedHotKey { Key = "Q" };
        var invalidLetter = new SavedHotKey { Ctrl = true, Key = "1" };
        var opened = 0;
        var copied = 0;
        using var hotKeys = new GlobalHotKeyManager(
            IntPtr.Zero,
            openBinding,
            copyBinding,
            () => opened++,
            () => copied++);
        _ = hotKeys.ProcessMessage(GlobalHotKeyManager.HotKeyMessage, new IntPtr(GlobalHotKeyManager.OpenLatestFolderId));
        _ = hotKeys.ProcessMessage(GlobalHotKeyManager.HotKeyMessage, new IntPtr(GlobalHotKeyManager.CopyLatestFolderPathId));
        if (!diagnostic.Contains(@"G:\  重连中", StringComparison.Ordinal)
            || !diagnostic.Contains("重试：3", StringComparison.Ordinal)
            || GlobalHotKeyManager.CopyLatestFolderPathId == GlobalHotKeyManager.OpenLatestFolderId
            || openBinding.DisplayText() != "Ctrl+Alt+F"
            || copyBinding.DisplayText() != "Ctrl+Alt+D"
            || !openBinding.TryValidate(out _)
            || invalidBinding.TryValidate(out _)
            || invalidLetter.TryValidate(out _)
            || openBinding.SameCombination(copyBinding)
            || opened != 1
            || copied != 1)
        {
            return 82;
        }

        var history = new ActivityHistoryStore();
        for (var folderIndex = 0; folderIndex < 60; folderIndex++)
        {
            for (var fileIndex = 0; fileIndex < 5; fileIndex++)
            {
                var folder = $@"C:\history\folder-{folderIndex:00}";
                history.Add(new ChangeRecord
                {
                    Time = new DateTime(2026, 8, 29, 11, 0, 0).AddSeconds(folderIndex * 10 + fileIndex),
                    ChangeKind = "更新",
                    FolderPath = folder,
                    FilePath = Path.Combine(folder, $"file-{fileIndex}.txt"),
                    ScopePath = @"C:\",
                    FileName = $"file-{fileIndex}.txt"
                });
            }
        }

        var saved = history.ToSettings();
        var restored = new ActivityHistoryStore();
        restored.Load(saved.Concat(new[] { new SavedActivityFolder { FolderPath = "::invalid::", LastTime = DateTime.Now } }));
        if (saved.Count != ActivityHistoryStore.MaxFolders
            || saved.Any(item => item.Files.Count > ActivityHistoryStore.MaxFilesPerFolder)
            || restored.Items.Count != ActivityHistoryStore.MaxFolders)
        {
            return 83;
        }

        return 0;
    }

    private static int RunPlacementTests()
    {
        var size = new Size(96, 80);
        var resolved = FloatingWindowPlacement.Resolve(new Point(-50000, -50000), size);
        var resolvedScreen = Screen.FromPoint(new Point(resolved.X + size.Width / 2, resolved.Y + size.Height / 2));
        if (!resolvedScreen.WorkingArea.Contains(new Rectangle(resolved, size)))
        {
            return 23;
        }

        var clamped = FloatingWindowPlacement.Clamp(new Point(50000, 50000), size);
        var clampedScreen = Screen.FromPoint(new Point(clamped.X + size.Width / 2, clamped.Y + size.Height / 2));
        return clampedScreen.WorkingArea.Contains(new Rectangle(clamped, size)) ? 0 : 24;
    }

    private static int RunFavoriteTests()
    {
        var store = new FavoriteFolderStore();
        var start = new DateTime(2026, 1, 1, 8, 0, 0);
        if (!store.Add(@"C:\favorites\project\assets", start)
            || !store.Add(@"C:\favorites\project", start.AddMinutes(1))
            || store.Add(@"c:\FAVORITES\PROJECT\", start.AddMinutes(2)))
        {
            return 25;
        }

        var activity = start.AddHours(2);
        if (!store.MarkActivity(@"C:\favorites\project\assets\images", activity))
        {
            return 26;
        }

        var updated = store.OrderedItems;
        if (updated.Count != 2
            || updated.Any(item => item.LastActivity != activity)
            || updated[0].Path != @"C:\favorites\project\assets"
            || updated[1].Path != @"C:\favorites\project")
        {
            return 27;
        }

        if (store.MarkActivity(@"C:\unrelated", activity.AddMinutes(1))
            || !store.Remove(@"C:\favorites\project\assets")
            || store.Count != 1
            || store.ToSettings().Count != 1)
        {
            return 28;
        }

        return 0;
    }

    private static int RunPinnedFolderTests()
    {
        var store = new PinnedFolderStore();
        var created = new DateTime(2026, 8, 11, 9, 0, 0);
        store.Load(new[]
        {
            new SavedPinnedFolder
            {
                Path = @"C:\pins\alpha",
                Left = 120,
                Top = 240,
                PinnedAt = created
            },
            new SavedPinnedFolder
            {
                Path = @"c:\PINS\ALPHA\",
                Left = 1,
                Top = 2,
                PinnedAt = created.AddMinutes(1)
            }
        });

        if (store.Count != 1
            || !store.Contains(@"C:\pins\alpha")
            || store.Items[0].Position != new Point(120, 240)
            || store.Pin(@"C:\pins\alpha"))
        {
            return 74;
        }

        if (!store.Pin(@"C:\pins\beta", new Point(360, 480))
            || !store.UpdatePosition(@"C:\pins\beta", new Point(400, 500))
            || store.UpdatePosition(@"C:\pins\beta", new Point(400, 500)))
        {
            return 75;
        }

        var activityTime = DateTime.Now.AddSeconds(1);
        if (!store.MarkActivity(@"C:\pins\beta\child", activityTime)
            || !store.Items.Single(item => item.Path == @"C:\pins\beta").HasUnreadActivity
            || !store.MarkOpened(@"C:\pins\beta", activityTime.AddSeconds(1))
            || store.Items.Single(item => item.Path == @"C:\pins\beta").HasUnreadActivity)
        {
            return 79;
        }

        var saved = store.ToSettings();
        if (saved.Count != 2
            || saved.Single(item => item.Path == @"C:\pins\beta").Left != 400
            || saved.Single(item => item.Path == @"C:\pins\beta").Top != 500
            || !store.Remove(@"C:\pins\alpha")
            || store.Contains(@"C:\pins\alpha"))
        {
            return 76;
        }

        return 0;
    }

    private static int RunWindowPresentationTests()
    {
        const int wmSysCommand = 0x0112;
        const int scRestore = 0xF120;
        const int scMinimize = 0xF020;
        if (MainForm.ShouldSuspendMainRendering(
                floatingModeActive: false,
                FormWindowState.Normal)
            || !MainForm.ShouldSuspendMainRendering(
                floatingModeActive: false,
                FormWindowState.Minimized)
            || !MainForm.ShouldSuspendMainRendering(
                floatingModeActive: true,
                FormWindowState.Normal)
            || !MainForm.ShouldRestoreFloatingMode(
                restoreFloatingAfterTaskbarMinimize: true,
                wmSysCommand,
                new IntPtr(scRestore))
            || MainForm.ShouldRestoreFloatingMode(
                restoreFloatingAfterTaskbarMinimize: false,
                wmSysCommand,
                new IntPtr(scRestore))
            || MainForm.ShouldRestoreFloatingMode(
                restoreFloatingAfterTaskbarMinimize: true,
                wmSysCommand,
                new IntPtr(scMinimize))
            || MainForm.ShouldRestoreFloatingMode(
                restoreFloatingAfterTaskbarMinimize: true,
                message: 0x0005,
                new IntPtr(scRestore)))
        {
            return 77;
        }

        const System.Reflection.BindingFlags privateInstance =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        return typeof(MainForm).GetMethod("EnterFloatingMode", privateInstance) is not null
            && typeof(MainForm).GetField("_openFloatingButton", privateInstance) is not null
            ? 0
            : 78;
    }

    private static int RunUiResourceTests()
    {
        var cards = new List<FolderCard>();
        var favoriteCards = new List<FavoriteFolderCard>();
        FloatingFolderForm? floating = null;
        PinnedFolderForm? pinnedFolder = null;
        LatestFileQuickForm? latestFileQuick = null;
        FavoriteQuickMenuForm? quickMenu = null;
        FavoriteInfoPopupForm? infoPopup = null;
        SettingsForm? settingsForm = null;
        ContextMenuStrip? contextMenu = null;
        var latestFolderOpenCount = 0;
        var pinnedFolderOpenCount = 0;
        var floatingMinimizeCount = 0;
        try
        {
            for (var index = 0; index < 6; index++)
            {
                var folder = new FolderActivity($@"C:\activity\folder-{index}");
                folder.Add(CreateRecord($@"C:\activity\folder-{index}\initial.txt", DateTime.Now));
                var card = new FolderCard(
                    folder,
                    expanded: true,
                    isLatest: index == 0,
                    width: 450,
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => false,
                    _ => { },
                    _ => false);
                card.CreateControl();
                cards.Add(card);
            }

            if (cards.Any(card =>
                    card.VisibleFileRowCount != 1
                    || card.FilePanelBounds.Top != 88
                    || card.FilePanelBounds.Bottom > card.ClientSize.Height))
            {
                return 42;
            }

            floating = new FloatingFolderForm(
                () => latestFolderOpenCount++,
                _ => { },
                _ => { },
                () => { },
                () => floatingMinimizeCount++,
                () => { },
                () => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                () => true,
                _ => false);
            floating.CreateControl();
            var minimizeItem = floating.ContextMenuStrip?.Items
                .OfType<ToolStripMenuItem>()
                .SingleOrDefault(item => item.Text == "最小化到任务栏");
            minimizeItem?.PerformClick();
            if (floating.LatestFileCount != 0
                || minimizeItem is null
                || floatingMinimizeCount != 1)
            {
                return 87;
            }

            floating.UpdateFavorites(Enumerable.Range(0, 14)
                .Select(index => new FavoriteFolder(
                    $@"C:\favorites\folder-{index}",
                    DateTime.Now,
                    DateTime.Now))
                .ToArray());

            var interactionFolder = new FolderActivity(@"C:\activity\latest");
            var interactionRecord = CreateRecord(@"C:\activity\latest\final.png", DateTime.Now);
            interactionFolder.Add(interactionRecord);
            floating.UpdateLatest(interactionFolder, interactionRecord, monitoring: true);
            floating.ShowAt(Screen.PrimaryScreen?.WorkingArea.Location);
            var toggleLatest = typeof(FloatingFolderForm).GetMethod(
                "ToggleLatestFileCard",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var activeDoubleClick = typeof(FloatingFolderForm).GetMethod(
                "HandleActiveDoubleClick",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (toggleLatest is null || activeDoubleClick is null)
            {
                return 88;
            }

            toggleLatest.Invoke(floating, null);
            if (!floating.LatestFileCardVisible)
            {
                return 89;
            }

            activeDoubleClick.Invoke(
                floating,
                new object?[] { floating, new MouseEventArgs(MouseButtons.Left, 2, 0, 0, 0) });
            if (latestFolderOpenCount != 1 || floating.LatestFileCardVisible)
            {
                return 91;
            }

            var showFavoriteInfo = typeof(FloatingFolderForm).GetMethod(
                "ShowFavoriteInfo",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (showFavoriteInfo is null)
            {
                return 92;
            }

            var distantAnchor = new Rectangle(Cursor.Position.X + 1000, Cursor.Position.Y + 1000, 1, 1);
            showFavoriteInfo.Invoke(floating, new object?[]
            {
                new FavoriteFolder(@"C:\favorites\watchdog", DateTime.Now, DateTime.Now),
                distantAnchor
            });
            var dismissDeadline = DateTime.UtcNow.AddMilliseconds(400);
            while (DateTime.UtcNow < dismissDeadline && floating.FavoriteInfoVisible)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }

            floating.Hide();
            if (floating.FavoriteInfoVisible || floating.LatestFileCardVisible)
            {
                return 93;
            }

            latestFileQuick = new LatestFileQuickForm(_ => { }, _ => { }, _ => { });
            latestFileQuick.UpdateFile(CreateRecord(@"C:\activity\latest\final.png", DateTime.Now));
            latestFileQuick.CreateControl();
            if (latestFileQuick.DisplayedFilePath != @"C:\activity\latest\final.png")
            {
                return 86;
            }

            pinnedFolder = new PinnedFolderForm(
                new PinnedFolder(@"C:\pins\alpha", DateTime.Now, new Point(72, 72)),
                _ => pinnedFolderOpenCount++,
                _ => { },
                _ => { },
                _ => { },
                (_, _) => { });
            pinnedFolder.CreateControl();
            pinnedFolder.ShowAt(new Point(72, 72));
            pinnedFolder.UpdatePinnedFolder(new PinnedFolder(
                @"C:\pins\alpha",
                DateTime.Now,
                new Point(96, 96)));
            if (!pinnedFolder.Visible
                || pinnedFolder.FolderPath != @"C:\pins\alpha"
                || pinnedFolder.Size != FloatingFrameAssets.PinnedFrame.Size
                || FloatingFrameAssets.PinnedFrame.Size != new Size(108, 90))
            {
                return 94;
            }
            pinnedFolder.Hide();

            contextMenu = new ContextMenuStrip();
            quickMenu = new FavoriteQuickMenuForm(
                _ => { },
                (_, _) => { },
                () => { },
                () => { },
                _ => { },
                contextMenu);
            var visibleFavorites = Enumerable.Range(1, 13)
                .Select(index => new FavoriteFolder(
                    $@"C:\favorites\folder-{index}",
                    DateTime.Now,
                    DateTime.Now))
                .ToArray();
            quickMenu.UpdateFavorites(visibleFavorites);
            quickMenu.PrepareForShow();
            quickMenu.Location = Screen.PrimaryScreen?.WorkingArea.Location ?? Point.Empty;
            quickMenu.Show();

            infoPopup = new FavoriteInfoPopupForm();
            infoPopup.ShowFor(visibleFavorites[0], new Rectangle(quickMenu.Right, quickMenu.Bottom - 90, 108, 90));
            settingsForm = new SettingsForm(
                Array.Empty<WatchScope>(),
                Array.Empty<string>(),
                floatingFavoriteCount: 8,
                startWithWindows: true);

            settingsForm.CreateControl();
            quickMenu.Refresh();
            infoPopup.Refresh();
            var itemTops = quickMenu.ItemTops;
            var orderedBottomUp = itemTops
                .Zip(itemTops.Skip(1), (lower, upper) => lower > upper)
                .All(isOrdered => isOrdered);
            if (!quickMenu.Visible
                || floating.FavoriteCount != 14
                || quickMenu.ItemCount != 13
                || itemTops.Count != 13
                || !orderedBottomUp
                || !infoPopup.Visible
                || settingsForm.FloatingFavoriteCount != 8
                || !settingsForm.StartWithWindows
                || settingsForm.OpenLatestFolderHotKey.DisplayText() != "Ctrl+Alt+F"
                || settingsForm.CopyLatestFolderPathHotKey.DisplayText() != "Ctrl+Alt+D"
                || !ContainsControlText(settingsForm, "打开最新文件夹")
                || !ContainsControlText(settingsForm, "复制最新文件夹地址")
                || !ContainsControlText(settingsForm, $"版本：{Application.ProductVersion}")
                || !ContainsControlText(settingsForm, "开发者：cslm"))
            {
                return 41;
            }

            for (var index = 0; index < 6; index++)
            {
                var favorite = new FavoriteFolder(
                    $@"C:\favorites\folder-{index}",
                    DateTime.Now,
                    DateTime.Now);
                var card = new FavoriteFolderCard(
                    favorite,
                    isLatest: index == 0,
                    width: 450,
                    _ => { },
                    _ => { },
                    _ => { });
                card.CreateControl();
                favoriteCards.Add(card);
            }

            var baseline = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
            for (var iteration = 0; iteration < 500; iteration++)
            {
                for (var index = 0; index < cards.Count; index++)
                {
                    var folder = new FolderActivity($@"C:\activity\folder-{index}");
                    folder.Add(CreateRecord(
                        $@"C:\activity\folder-{index}\file-{iteration % 3}.txt",
                        DateTime.Now.AddMilliseconds(iteration)));
                    cards[index].UpdateActivity(
                        folder,
                        expanded: true,
                        isLatest: index == 0,
                        width: 450 + iteration % 3);

                    if (index == 0)
                    {
                        floating.UpdateLatest(folder, folder.Files[0], monitoring: true);
                    }


                    favoriteCards[index].UpdateFavorite(
                        new FavoriteFolder(
                            $@"C:\favorites\folder-{index}",
                            DateTime.Now,
                            DateTime.Now.AddMilliseconds(iteration)),
                        isLatest: index == 0,
                        width: 450 + iteration % 3);
                }

                floating.UpdateFavorites(Enumerable.Range(0, 14)
                    .Select(index => new FavoriteFolder(
                        $@"C:\favorites\folder-{(index + iteration) % 5}",
                        DateTime.Now,
                        DateTime.Now.AddMilliseconds(iteration)))
                    .ToArray());

                if (iteration % 5 == 0)
                {
                    quickMenu.UpdateFavorites(Enumerable.Range(0, 13)
                        .Select(index => new FavoriteFolder(
                            $@"C:\favorites\folder-{(index + iteration / 5) % 13 + 1}",
                            DateTime.Now,
                            DateTime.Now.AddMilliseconds(iteration)))
                        .ToArray());
                }

            }

            var afterUpdates = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
            return afterUpdates <= baseline + 10
                && quickMenu.ItemCount == 13
                && floating.LatestFileCount == 1
                ? 0
                : 40;
        }
        finally
        {
            foreach (var card in cards)
            {
                card.Dispose();
            }

            floating?.Dispose();
            pinnedFolder?.Dispose();
            latestFileQuick?.Dispose();
            infoPopup?.Dispose();
            settingsForm?.Dispose();
            quickMenu?.Dispose();
            contextMenu?.Dispose();
            foreach (var card in favoriteCards)
            {
                card.Dispose();
            }

            ShellIconProvider.DisposeCache();
        }
    }

    private static bool ContainsControlText(Control root, string text)
    {
        return string.Equals(root.Text, text, StringComparison.Ordinal)
            || root.Controls.Cast<Control>().Any(child => ContainsControlText(child, text));
    }

    private static ChangeRecord CreateRecord(string filePath, DateTime time)
    {
        return new ChangeRecord
        {
            Time = time,
            ChangeKind = "更新",
            FolderPath = Path.GetDirectoryName(filePath) ?? string.Empty,
            FilePath = filePath,
            ScopePath = Path.GetPathRoot(filePath) ?? string.Empty,
            FileName = Path.GetFileName(filePath)
        };
    }

    private static bool WaitForRecord(
        ConcurrentBag<ChangeRecord> records,
        string filePath,
        int minimumCount,
        TimeSpan timeout)
    {
        return SpinWait.SpinUntil(() => CountFor(records, filePath) >= minimumCount, timeout);
    }

    private static int CountFor(ConcurrentBag<ChangeRecord> records, string filePath)
    {
        return records.Count(record => string.Equals(record.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
    }

    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);
}
