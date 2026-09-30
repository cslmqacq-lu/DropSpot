namespace DropSpot;

public static class AppLog
{
    private const long MaxFileBytes = 1024 * 1024;
    private const int MaxArchivedFiles = 4;
    private const int MaxRecentEntries = 100;
    private static readonly object Sync = new();
    private static readonly Queue<string> Recent = new();

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DropSpot",
        "logs");

    /// <summary>日志文件名；后台监视进程使用独立文件，避免两个进程同时写一个文件。</summary>
    public static string FileName { get; set; } = "DropSpot.log";

    public static string CurrentLogPath => Path.Combine(LogDirectory, FileName);

    public static IReadOnlyList<string> RecentEntries
    {
        get
        {
            lock (Sync)
            {
                return Recent.ToArray();
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warning(string message) => Write("WARN", message);
    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");
    }

    internal static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (Sync)
        {
            Recent.Enqueue(line);
            while (Recent.Count > MaxRecentEntries)
            {
                Recent.Dequeue();
            }

            try
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                File.AppendAllText(CurrentLogPath, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never prevent monitoring or shutdown.
            }
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(CurrentLogPath) || new FileInfo(CurrentLogPath).Length < MaxFileBytes)
        {
            return;
        }

        for (var index = MaxArchivedFiles; index >= 1; index--)
        {
            var source = index == 1 ? CurrentLogPath : CurrentLogPath + $".{index - 1}";
            var target = CurrentLogPath + $".{index}";
            if (File.Exists(source))
            {
                File.Move(source, target, overwrite: true);
            }
        }
    }
}
