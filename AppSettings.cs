using System.Text.Json;

namespace DiskWriteWatcher;

public sealed class AppSettings
{
    public const int DefaultFloatingFavoriteCount = 5;
    public const int MinFloatingFavoriteCount = 1;
    public const int MaxFloatingFavoriteCount = 14;
    public const int DefaultFloatingBackgroundArgb = unchecked((int)0xFF080C12);
    public const int DefaultFloatingOpacityPercent = 70;
    public const int MinFloatingOpacityPercent = 20;
    public const int MaxFloatingOpacityPercent = 100;

    public List<SavedWatchScope> WatchScopes { get; set; } = new();
    public List<string> ExcludedPaths { get; set; } = new();
    public List<SavedFavoriteFolder> FavoriteFolders { get; set; } = new();
    public int? FloatingLeft { get; set; }
    public int? FloatingTop { get; set; }
    public int FloatingFavoriteCount { get; set; } = DefaultFloatingFavoriteCount;
    public int FloatingBackgroundArgb { get; set; } = DefaultFloatingBackgroundArgb;
    public int FloatingOpacityPercent { get; set; } = DefaultFloatingOpacityPercent;

    public static int NormalizeFloatingFavoriteCount(int value)
    {
        return Math.Clamp(value, MinFloatingFavoriteCount, MaxFloatingFavoriteCount);
    }

    public static int NormalizeFloatingOpacityPercent(int value)
    {
        return Math.Clamp(value, MinFloatingOpacityPercent, MaxFloatingOpacityPercent);
    }

    public static Color GetFloatingBackgroundColor(int argb)
    {
        var source = Color.FromArgb(argb);
        var color = Color.FromArgb(255, source.R, source.G, source.B);
        return color.ToArgb() == Color.Fuchsia.ToArgb()
            ? Color.FromArgb(254, 0, 255)
            : color;
    }

    public static AppSettings Load()
    {
        return Load(out _);
    }

    public static AppSettings Load(out string? warning)
    {
        return LoadFrom(SettingsPath, out warning);
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

    private static string SettingsPath =>
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
}
