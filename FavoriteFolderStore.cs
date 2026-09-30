namespace DropSpot;

public sealed class FavoriteFolderStore
{
    private readonly Dictionary<string, FavoriteFolder> _items = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _items.Count;

    public IReadOnlyList<FavoriteFolder> OrderedItems => _items.Values
        .OrderByDescending(item => item.LastActivity)
        .ThenByDescending(item => item.Path.Length)
        .ThenByDescending(item => item.AddedAt)
        .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public void Load(IEnumerable<SavedFavoriteFolder> savedItems)
    {
        _items.Clear();
        foreach (var saved in savedItems)
        {
            if (!TryNormalizePath(saved.Path, out var path))
            {
                continue;
            }

            var addedAt = saved.AddedAt == default ? DateTime.Now : saved.AddedAt;
            var lastActivity = saved.LastActivity == default ? addedAt : saved.LastActivity;
            if (!_items.TryGetValue(path, out var folder))
            {
                folder = new FavoriteFolder(path, addedAt, lastActivity);
                _items.Add(path, folder);
            }
            else
            {
                folder.Merge(addedAt, lastActivity);
            }

            foreach (var file in saved.Files ?? new List<SavedFavoriteFile>())
            {
                if (TryNormalizePath(file.Path, out var filePath) && IsPathUnder(filePath, path))
                {
                    folder.AddFile(filePath, file.AddedAt == default ? addedAt : file.AddedAt);
                }
            }
        }
    }

    /// <summary>所有收藏文件，最近收藏的在前。</summary>
    public IReadOnlyList<FavoriteFile> AllFiles => _items.Values
        .SelectMany(folder => folder.Files)
        .OrderByDescending(file => file.AddedAt)
        .ToArray();

    public int FileCount => _items.Values.Sum(folder => folder.Files.Count);

    public bool ContainsFile(string filePath)
    {
        return TryNormalizePath(filePath, out var normalized)
            && _items.Values.Any(folder => folder.ContainsFile(normalized));
    }

    /// <summary>
    /// 收藏一个文件：归到路径最深的已收藏上级文件夹下；没有的话先自动收藏它所在的文件夹。
    /// </summary>
    public FavoriteFileResult AddFile(string filePath, DateTime now)
    {
        if (!TryNormalizePath(filePath, out var normalized))
        {
            return new FavoriteFileResult(null, FolderCreated: false, AlreadyFavorite: false);
        }

        var owner = _items.Values
            .Where(folder => IsPathUnder(normalized, folder.Path) && !string.Equals(normalized, folder.Path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(folder => folder.Path.Length)
            .FirstOrDefault();
        var folderCreated = false;
        if (owner is null)
        {
            var parent = System.IO.Path.GetDirectoryName(normalized);
            if (string.IsNullOrWhiteSpace(parent) || !TryNormalizePath(parent, out var parentPath))
            {
                return new FavoriteFileResult(null, FolderCreated: false, AlreadyFavorite: false);
            }

            owner = new FavoriteFolder(parentPath, now, now);
            _items.Add(parentPath, owner);
            folderCreated = true;
        }

        var added = owner.AddFile(normalized, now);
        if (added)
        {
            owner.MarkActivity(now);
        }

        return new FavoriteFileResult(owner, folderCreated, AlreadyFavorite: !added);
    }

    public bool RemoveFile(string filePath)
    {
        if (!TryNormalizePath(filePath, out var normalized))
        {
            return false;
        }

        var removed = false;
        foreach (var folder in _items.Values)
        {
            removed |= folder.RemoveFile(normalized);
        }

        return removed;
    }

    public bool Add(string path, DateTime lastActivity)
    {
        if (!TryNormalizePath(path, out var normalized) || _items.ContainsKey(normalized))
        {
            return false;
        }

        var now = DateTime.Now;
        _items.Add(normalized, new FavoriteFolder(normalized, now, lastActivity == default ? now : lastActivity));
        return true;
    }

    public bool Remove(string path)
    {
        return TryNormalizePath(path, out var normalized) && _items.Remove(normalized);
    }

    public bool Contains(string path)
    {
        return TryNormalizePath(path, out var normalized) && _items.ContainsKey(normalized);
    }

    public bool MarkActivity(string changedFolderPath, DateTime activityTime)
    {
        if (!TryNormalizePath(changedFolderPath, out var changedPath))
        {
            return false;
        }

        var changed = false;
        foreach (var favorite in _items.Values)
        {
            if (IsPathUnder(changedPath, favorite.Path) && favorite.MarkActivity(activityTime))
            {
                changed = true;
            }
        }

        return changed;
    }

    public List<SavedFavoriteFolder> ToSettings()
    {
        return _items.Values.Select(item => new SavedFavoriteFolder
        {
            Path = item.Path,
            AddedAt = item.AddedAt,
            LastActivity = item.LastActivity,
            Files = item.Files.Select(file => new SavedFavoriteFile
            {
                Path = file.Path,
                AddedAt = file.AddedAt
            }).ToList()
        }).ToList();
    }

    private static bool IsPathUnder(string path, string parent)
    {
        var prefix = parent.EndsWith(Path.DirectorySeparatorChar)
            ? parent
            : parent + Path.DirectorySeparatorChar;
        return string.Equals(path, parent, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizePath(string path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            normalized = string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                ? fullPath.TrimEnd(Path.AltDirectorySeparatorChar)
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return !string.IsNullOrWhiteSpace(normalized);
        }
        catch
        {
            return false;
        }
    }
}

public sealed class FavoriteFolder
{
    public FavoriteFolder(string path, DateTime addedAt, DateTime lastActivity)
    {
        Path = path;
        AddedAt = addedAt;
        LastActivity = lastActivity;
    }

    private readonly List<FavoriteFile> _files = new();

    public string Path { get; }
    public DateTime AddedAt { get; private set; }

    /// <summary>这个文件夹下被标记收藏的文件，最近收藏的在前。</summary>
    public IReadOnlyList<FavoriteFile> Files => _files;

    public bool ContainsFile(string filePath) =>
        _files.Any(file => string.Equals(file.Path, filePath, StringComparison.OrdinalIgnoreCase));

    public bool AddFile(string filePath, DateTime addedAt)
    {
        if (ContainsFile(filePath))
        {
            return false;
        }

        _files.Add(new FavoriteFile(filePath, this.Path, addedAt));
        _files.Sort((left, right) => right.AddedAt.CompareTo(left.AddedAt));
        return true;
    }

    public bool RemoveFile(string filePath) =>
        _files.RemoveAll(file => string.Equals(file.Path, filePath, StringComparison.OrdinalIgnoreCase)) > 0;
    public DateTime LastActivity { get; private set; }

    public string DisplayName
    {
        get
        {
            var trimmed = Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var name = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? Path : name;
        }
    }

    public bool MarkActivity(DateTime time)
    {
        if (time <= LastActivity)
        {
            return false;
        }

        LastActivity = time;
        return true;
    }

    public void Merge(DateTime addedAt, DateTime lastActivity)
    {
        if (addedAt < AddedAt)
        {
            AddedAt = addedAt;
        }

        if (lastActivity > LastActivity)
        {
            LastActivity = lastActivity;
        }
    }
}

public sealed record FavoriteFileResult(FavoriteFolder? Folder, bool FolderCreated, bool AlreadyFavorite);

public sealed class FavoriteFile
{
    public FavoriteFile(string path, string folderPath, DateTime addedAt)
    {
        Path = path;
        FolderPath = folderPath;
        AddedAt = addedAt;
    }

    public string Path { get; }

    /// <summary>所属的收藏文件夹。</summary>
    public string FolderPath { get; }

    public DateTime AddedAt { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>相对所属收藏文件夹的路径，如“子目录\报告.docx”。</summary>
    public string RelativeName
    {
        get
        {
            var relative = System.IO.Path.GetRelativePath(FolderPath, Path);
            return string.IsNullOrWhiteSpace(relative) || relative.StartsWith("..", StringComparison.Ordinal)
                ? FileName
                : relative;
        }
    }
}
