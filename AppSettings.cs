using System.Text.Json;

namespace DropSpot;

public sealed class AppSettings
{
    public const int DefaultFloatingFavoriteCount = 5;
    public const int MinFloatingFavoriteCount = 1;
    public const int MaxFloatingFavoriteCount = 14;
    public const int DefaultCapsuleOpacity = 88;
    public const int MinCapsuleOpacity = 50;
    public const int MaxCapsuleOpacity = 100;

    public List<SavedWatchScope> WatchScopes { get; set; } = new();
    public List<string> ExcludedPaths { get; set; } = new();
    public List<SavedFavoriteFolder> FavoriteFolders { get; set; } = new();
    public List<SavedPinnedFolder> PinnedFolders { get; set; } = new();
    public List<SavedActivityFolder> ActivityHistory { get; set; } = new();
    public int? FloatingLeft { get; set; }
    public int? FloatingTop { get; set; }
    public int FloatingFavoriteCount { get; set; } = DefaultFloatingFavoriteCount;
    public bool StartWithWindows { get; set; }
    /// <summary>过滤 .git、node_modules、浏览器缓存等常见噪音目录。</summary>
    public bool FilterCommonNoise { get; set; } = true;
    /// <summary>打开活跃文件夹时，在资源管理器中选中最新的文件。</summary>
    public bool SelectLatestFileWhenOpeningFolder { get; set; } = true;
    /// <summary>隐藏开发 / AI 编程产生的文件（编译产物、缓存、日志、锁文件、obj 目录）。</summary>
    public bool FilterDevFiles { get; set; } = true;
    /// <summary>用户额外隐藏的扩展名，如 ".tmp"、".log"。</summary>
    public List<string> HiddenExtensions { get; set; } = new();
    /// <summary>悬浮舱背景不透明度（百分比，50–100）。文字始终不透明。</summary>
    public int CapsuleOpacity { get; set; } = DefaultCapsuleOpacity;
    public SavedHotKey OpenLatestFolderHotKey { get; set; } = SavedHotKey.OpenLatestFolderDefault();
    public SavedHotKey CopyLatestFolderPathHotKey { get; set; } = SavedHotKey.CopyLatestFolderPathDefault();

    public static int NormalizeFloatingFavoriteCount(int value)
    {
        return Math.Clamp(value, MinFloatingFavoriteCount, MaxFloatingFavoriteCount);
    }

    public static int NormalizeCapsuleOpacity(int value)
    {
        return Math.Clamp(value, MinCapsuleOpacity, MaxCapsuleOpacity);
    }

    public static AppSettings Load()
    {
        return Load(out _);
    }

    public static AppSettings Load(out string? warning)
    {
        _ = MigrateLegacySettings(LegacySettingsPath, SettingsPath, out var migrationWarning);
        var settings = LoadFrom(SettingsPath, out warning);
        if (!string.IsNullOrWhiteSpace(migrationWarning))
        {
            warning = string.IsNullOrWhiteSpace(warning)
                ? migrationWarning
                : $"{migrationWarning}；{warning}";
        }

        return settings;
    }

    internal static bool MigrateLegacySettings(string legacyPath, string currentPath, out string? warning)
    {
        warning = null;
        if (File.Exists(currentPath) || File.Exists(currentPath + ".bak"))
        {
            return false;
        }

        var legacyBackupPath = legacyPath + ".bak";
        if (!File.Exists(legacyPath) && !File.Exists(legacyBackupPath))
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(legacyPath))
            {
                File.Copy(legacyPath, currentPath, overwrite: false);
            }

            if (File.Exists(legacyBackupPath))
            {
                File.Copy(legacyBackupPath, currentPath + ".bak", overwrite: false);
            }

            warning = "已从旧版 DiskWriteWatcher 迁移设置";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning = $"旧版设置迁移失败：{ex.Message}";
            return false;
        }
    }

    internal static AppSettings LoadFrom(string path, out string? warning)
    {
        warning = null;
        var backupPath = path + ".bak";
        if (!File.Exists(path))
        {
            if (TryLoad(backupPath, out var backupSettings))
            {
                warning = "设置文件缺失，已从备份恢复";
                return backupSettings;
            }

            return new AppSettings();
        }

        if (TryLoad(path, out var settings))
        {
            return settings;
        }

        if (TryLoad(backupPath, out settings))
        {
            warning = "设置文件损坏，已从备份恢复";
            return settings;
        }

        warning = "设置文件损坏，已使用默认设置";
        return new AppSettings();
    }

    public void Save()
    {
        SaveTo(SettingsPath);
    }

    internal void SaveTo(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var tempPath = path + ".tmp";
        var backupPath = path + ".bak";
        try
        {
            File.WriteAllText(tempPath, json);
            _ = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(tempPath))
                ?? throw new InvalidDataException("设置序列化验证失败");

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    ReplaceWithFallback(tempPath, path, backupPath);
                }
                catch (IOException)
                {
                    ReplaceWithFallback(tempPath, path, backupPath);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static bool TryLoad(string path, out AppSettings settings)
    {
        settings = new AppSettings();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            settings.Normalize();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void ReplaceWithFallback(string tempPath, string path, string backupPath)
    {
        File.Copy(path, backupPath, overwrite: true);
        File.Move(tempPath, path, overwrite: true);
    }

    private void Normalize()
    {
        CapsuleOpacity = NormalizeCapsuleOpacity(CapsuleOpacity);
        OpenLatestFolderHotKey = SavedHotKey.Normalize(
            OpenLatestFolderHotKey,
            SavedHotKey.OpenLatestFolderDefault());
        CopyLatestFolderPathHotKey = SavedHotKey.Normalize(
            CopyLatestFolderPathHotKey,
            SavedHotKey.CopyLatestFolderPathDefault());
    }

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DropSpot",
            "settings.json");

    private static string LegacySettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiskWriteWatcher",
            "settings.json");
}

public sealed class SavedWatchScope
{
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class SavedFavoriteFolder
{
    public string Path { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public DateTime LastActivity { get; set; }
    /// <summary>归在这个收藏文件夹下、被单独标记收藏的文件。</summary>
    public List<SavedFavoriteFile> Files { get; set; } = new();
}

public sealed class SavedFavoriteFile
{
    public string Path { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
}

public sealed class SavedPinnedFolder
{
    public string Path { get; set; } = string.Empty;
    public int? Left { get; set; }
    public int? Top { get; set; }
    public DateTime PinnedAt { get; set; }
    public DateTime LastActivity { get; set; }
    public DateTime LastOpenedAt { get; set; }
}

public sealed class SavedActivityFolder
{
    public string FolderPath { get; set; } = string.Empty;
    public DateTime LastTime { get; set; }
    public int ChangeCount { get; set; }
    public List<SavedActivityFile> Files { get; set; } = new();
}

public sealed class SavedActivityFile
{
    public DateTime Time { get; set; }
    public string ChangeKind { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ScopePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

public sealed class SavedHotKey
{
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public string Key { get; set; } = string.Empty;

    public static SavedHotKey OpenLatestFolderDefault() => new()
    {
        Ctrl = true,
        Alt = true,
        Key = "F"
    };

    public static SavedHotKey CopyLatestFolderPathDefault() => new()
    {
        Ctrl = true,
        Alt = true,
        Key = "D"
    };

    public SavedHotKey Clone() => new()
    {
        Ctrl = Ctrl,
        Alt = Alt,
        Shift = Shift,
        Key = Key
    };

    public bool TryValidate(out string error)
    {
        if (!Ctrl && !Alt && !Shift)
        {
            error = "至少选择 Ctrl、Alt、Shift 中的一个修饰键";
            return false;
        }

        if (!TryGetVirtualKey(out _))
        {
            error = "请选择 A-Z 之间的字母";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryGetVirtualKey(out uint virtualKey)
    {
        var normalized = NormalizeLetter(Key);
        if (normalized is null)
        {
            virtualKey = 0;
            return false;
        }

        virtualKey = normalized[0];
        return true;
    }

    public bool SameCombination(SavedHotKey other)
    {
        return Ctrl == other.Ctrl
            && Alt == other.Alt
            && Shift == other.Shift
            && string.Equals(NormalizeLetter(Key), NormalizeLetter(other.Key), StringComparison.Ordinal);
    }

    public string DisplayText()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(NormalizeLetter(Key) ?? "?");
        return string.Join("+", parts);
    }

    public static SavedHotKey Normalize(SavedHotKey? value, SavedHotKey fallback)
    {
        if (value is null || !value.TryValidate(out _))
        {
            return fallback.Clone();
        }

        var normalized = value.Clone();
        normalized.Key = NormalizeLetter(normalized.Key)!;
        return normalized;
    }

    private static string? NormalizeLetter(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        return trimmed is { Length: 1 } && trimmed[0] is >= 'A' and <= 'Z' ? trimmed : null;
    }
}
