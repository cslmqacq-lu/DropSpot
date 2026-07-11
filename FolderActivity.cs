namespace DropSpot;

public sealed class FolderActivity
{
    private const int MaxRecentFiles = 25;
    private readonly List<ChangeRecord> _files = new();

    public FolderActivity(string folderPath)
    {
        FolderPath = folderPath;
    }

    public string FolderPath { get; }
    public DateTime LastTime { get; private set; }
    public int ChangeCount { get; private set; }
    public IReadOnlyList<ChangeRecord> Files => _files;

    public string DisplayName
    {
        get
        {
            var trimmed = FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? FolderPath : name;
        }
    }

    public void Add(ChangeRecord record)
    {
        LastTime = record.Time;
        ChangeCount++;

        _files.RemoveAll(item => string.Equals(item.FilePath, record.FilePath, StringComparison.OrdinalIgnoreCase));
        _files.Insert(0, record);

        if (_files.Count > MaxRecentFiles)
        {
            _files.RemoveRange(MaxRecentFiles, _files.Count - MaxRecentFiles);
        }
    }
}
