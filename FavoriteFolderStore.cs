namespace DiskWriteWatcher;

public sealed class FavoriteFolderStore
{
    private readonly Dictionary<string, FavoriteFolder> _items = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _items.Count;

    public IReadOnlyList<FavoriteFolder> OrderedItems => _items.Values
        .OrderByDescending(item => item.LastActivity)
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
            if (_items.TryGetValue(path, out var existing))
            {
                existing.Merge(addedAt, lastActivity);
            }
            else
            {
                _items.Add(path, new FavoriteFolder(path, addedAt, lastActivity));
            }
        }
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
            LastActivity = item.LastActivity
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

    public string Path { get; }
    public DateTime AddedAt { get; private set; }
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
