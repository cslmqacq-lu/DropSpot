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
        monitor.Start(new[] { new WatchScope(tempRoot) });

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
        Thread.Sleep(1200);
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
        return command == "\"C:\\Program Files\\DropSpot\\DropSpot.exe\" --startup" ? 0 : 62;
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
            FloatingLeft = 123,
            FloatingTop = 456,
            FloatingFavoriteCount = 8,
            FloatingBackgroundArgb = Color.FromArgb(32, 64, 96).ToArgb(),
            FloatingOpacityPercent = 55,
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
            || recovered.FloatingLeft != 123
            || recovered.FloatingTop != 456
            || recovered.FloatingFavoriteCount != 8
            || recovered.FloatingBackgroundArgb != Color.FromArgb(32, 64, 96).ToArgb()
            || recovered.FloatingOpacityPercent != 55
            || !recovered.StartWithWindows
            || AppSettings.NormalizeFloatingFavoriteCount(0) != 1
            || AppSettings.NormalizeFloatingFavoriteCount(99) != 14
            || AppSettings.NormalizeFloatingOpacityPercent(0) != 20
            || AppSettings.NormalizeFloatingOpacityPercent(120) != 100
            || AppSettings.GetFloatingBackgroundColor(Color.Fuchsia.ToArgb()).ToArgb() == Color.Fuchsia.ToArgb())
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
        return warning is null
            && migrated.WatchScopes.Count == 1
            && migrated.WatchScopes[0].Path == @"C:\"
            ? 0
            : 35;
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

    private static int RunUiResourceTests()
    {
        var cards = new List<FolderCard>();
        var favoriteCards = new List<FavoriteFolderCard>();
        FloatingFolderForm? floating = null;
        FavoriteQuickMenuForm? quickMenu = null;
        FavoriteInfoPopupForm? infoPopup = null;
        SettingsForm? settingsForm = null;
        ContextMenuStrip? contextMenu = null;
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
                () => { },
                _ => { },
                () => { },
                () => { },
                () => { },
                _ => { },
                _ => { },
                _ => { },
                () => true);
            floating.CreateControl();
            var appearanceColor = Color.FromArgb(230, 235, 240);
            floating.ApplyAppearance(appearanceColor, 20);
            floating.UpdateFavorites(Enumerable.Range(0, 14)
                .Select(index => new FavoriteFolder(
                    $@"C:\favorites\folder-{index}",
                    DateTime.Now,
                    DateTime.Now))
                .ToArray());

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
            quickMenu.ApplyAppearance(appearanceColor, 20);
            quickMenu.PrepareForShow();
            quickMenu.Location = Screen.PrimaryScreen?.WorkingArea.Location ?? Point.Empty;
            quickMenu.Show();

            infoPopup = new FavoriteInfoPopupForm();
            infoPopup.ApplyAppearance(appearanceColor, 20);
            infoPopup.ShowFor(visibleFavorites[0], new Rectangle(quickMenu.Right, quickMenu.Bottom - 70, 101, 70));
            settingsForm = new SettingsForm(
                Array.Empty<WatchScope>(),
                Array.Empty<string>(),
                floatingFavoriteCount: 8,
                floatingBackgroundArgb: appearanceColor.ToArgb(),
                floatingOpacityPercent: 20,
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
                || floating.SurfaceColor.ToArgb() != appearanceColor.ToArgb()
                || floating.AppearanceOpacityPercent != 20
                || quickMenu.ItemCount != 13
                || quickMenu.AppearanceBackgroundColor.ToArgb() != appearanceColor.ToArgb()
                || quickMenu.AppearanceOpacityPercent != 20
                || itemTops.Count != 13
                || !orderedBottomUp
                || !infoPopup.Visible
                || infoPopup.SurfaceColor.ToArgb() != appearanceColor.ToArgb()
                || infoPopup.AppearanceOpacityPercent != 20
                || settingsForm.FloatingFavoriteCount != 8
                || settingsForm.FloatingBackgroundArgb != appearanceColor.ToArgb()
                || settingsForm.FloatingOpacityPercent != 20
                || !settingsForm.StartWithWindows
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
                        floating.UpdateLatest(folder, monitoring: true);
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
            return afterUpdates <= baseline + 10 && quickMenu.ItemCount == 13 ? 0 : 40;
        }
        finally
        {
            foreach (var card in cards)
            {
                card.Dispose();
            }

            floating?.Dispose();
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
