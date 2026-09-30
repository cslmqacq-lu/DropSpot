namespace DropSpot;

public static class LatestFileSelector
{
    private static readonly HashSet<string> TemporaryExtensions = new(
        new[] { ".tmp", ".temp", ".part", ".crdownload", ".download" },
        StringComparer.OrdinalIgnoreCase);

    public static ChangeRecord? SelectLatestExisting(
        IReadOnlyList<ChangeRecord> records,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(fileExists);

        return records
            .Where(record => !string.IsNullOrWhiteSpace(record.FilePath))
            .Where(record => !IsTemporaryFile(record.FilePath))
            .OrderByDescending(record => record.Time)
            .FirstOrDefault(record => fileExists(record.FilePath));
    }

    public static bool IsTemporaryFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return true;
        }

        var name = Path.GetFileName(filePath);
        return name.StartsWith("~$", StringComparison.OrdinalIgnoreCase)
            || TemporaryExtensions.Contains(Path.GetExtension(name))
            || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".temp", StringComparison.OrdinalIgnoreCase);
    }
}
