namespace DropSpot;

public sealed class ActivityHistoryStore
{
    public const int MaxFolders = 50;
    public const int MaxFilesPerFolder = 3;
    private readonly Dictionary<string, FolderActivity> _items = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FolderActivity> Items => _items.Values
        .OrderByDescending(item => item.LastTime)
        .ThenBy(item => item.FolderPath, StringComparer.OrdinalIgnoreCase)
        .Take(MaxFolders)
        .ToArray();

    public void Load(IEnumerable<SavedActivityFolder>? savedFolders)
    {
        _items.Clear();
        foreach (var saved in (savedFolders ?? Array.Empty<SavedActivityFolder>())
                     .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath) && item.LastTime != default)
                     .OrderByDescending(item => item.LastTime))
        {
            if (_items.Count >= MaxFolders)
            {
                break;
            }

            if (!TryNormalize(saved.FolderPath, out var folderPath) || _items.ContainsKey(folderPath))
            {
                continue;
            }

            var records = (saved.Files ?? new List<SavedActivityFile>())
                .Where(file => !string.IsNullOrWhiteSpace(file.FilePath))
                .OrderByDescending(file => file.Time)
                .Take(MaxFilesPerFolder)
                .Select(file => new ChangeRecord
                {
                    Time = file.Time == default ? saved.LastTime : file.Time,
                    ChangeKind = string.IsNullOrWhiteSpace(file.ChangeKind) ? "更新" : file.ChangeKind,
                    FolderPath = folderPath,
                    FilePath = file.FilePath,
                    ScopePath = string.IsNullOrWhiteSpace(file.ScopePath) ? Path.GetPathRoot(folderPath) ?? folderPath : file.ScopePath,
                    FileName = string.IsNullOrWhiteSpace(file.FileName) ? Path.GetFileName(file.FilePath) : file.FileName
                })
                .ToArray();
            _items.Add(folderPath, FolderActivity.Restore(folderPath, saved.LastTime, Math.Max(saved.ChangeCount, records.Length), records));
        }
    }

    public FolderActivity Add(ChangeRecord record)
    {
        if (!_items.TryGetValue(record.FolderPath, out var activity))
        {
            activity = new FolderActivity(record.FolderPath);
            _items.Add(record.FolderPath, activity);
        }

        activity.Add(record);
        Trim();
        return activity;
    }

    public void Clear() => _items.Clear();

    public int RemoveWhere(Func<string, bool> predicate)
    {
        var paths = _items.Keys.Where(predicate).ToArray();
        foreach (var path in paths)
        {
            _items.Remove(path);
        }

        return paths.Length;
    }

    public List<SavedActivityFolder> ToSettings()
    {
        return Items.Select(activity => new SavedActivityFolder
        {
            FolderPath = activity.FolderPath,
            LastTime = activity.LastTime,
            ChangeCount = activity.ChangeCount,
            Files = activity.Files.Take(MaxFilesPerFolder).Select(file => new SavedActivityFile
            {
                Time = file.Time,
                ChangeKind = file.ChangeKind,
                FilePath = file.FilePath,
                ScopePath = file.ScopePath,
                FileName = file.FileName
            }).ToList()
        }).ToList();
    }

    private void Trim()
    {
        if (_items.Count <= MaxFolders)
        {
            return;
        }

        foreach (var path in _items.Values
                     .OrderBy(item => item.LastTime)
                     .Take(_items.Count - MaxFolders)
                     .Select(item => item.FolderPath)
                     .ToArray())
        {
            _items.Remove(path);
        }
    }

    private static bool TryNormalize(string path, out string normalized)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                normalized = string.Empty;
                return false;
            }

            normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return !string.IsNullOrWhiteSpace(normalized);
        }
        catch
        {
            normalized = string.Empty;
            return false;
        }
    }
}
