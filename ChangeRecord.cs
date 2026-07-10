namespace DiskWriteWatcher;

public sealed class ChangeRecord
{
    public required DateTime Time { get; init; }
    public required string ChangeKind { get; init; }
    public required string FolderPath { get; init; }
    public required string FilePath { get; init; }
    public required string ScopePath { get; init; }
    public required string FileName { get; init; }
}
