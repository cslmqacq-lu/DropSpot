namespace DropSpot;

public static class LatestFileSelector
{
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

    /// <summary>与监视服务使用同一套临时文件规则（见 <see cref="PathRules.IsTemporaryFileName"/>）。</summary>
    public static bool IsTemporaryFile(string filePath)
    {
        return PathRules.IsTemporaryFileName(filePath);
    }
}
