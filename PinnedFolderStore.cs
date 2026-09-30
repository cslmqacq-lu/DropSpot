namespace DropSpot;

public sealed class PinnedFolderStore
{
    private readonly Dictionary<string, PinnedFolder> _items = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _items.Count;

    public IReadOnlyList<PinnedFolder> Items => _items.Values
        .OrderBy(item => item.PinnedAt)
        .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public void Load(IEnumerable<SavedPinnedFolder> savedItems)
    {
        _items.Clear();
        foreach (var saved in savedItems)
        {
            if (!TryNormalizePath(saved.Path, out var path) || _items.ContainsKey(path))
            {
                continue;
            }

            var pinnedAt = saved.PinnedAt == default ? DateTime.Now : saved.PinnedAt;
            Point? position = saved.Left is int left && saved.Top is int top ? new Point(left, top) : null;
            _items.Add(path, new PinnedFolder(path, pinnedAt, position, saved.LastActivity, saved.LastOpenedAt));
        }
    }

    public bool Pin(string path, Point? position = null)
    {
        if (!TryNormalizePath(path, out var normalized) || _items.ContainsKey(normalized))
        {
            return false;
        }

        _items.Add(normalized, new PinnedFolder(normalized, DateTime.Now, position, default, DateTime.Now));
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

    public bool UpdatePosition(string path, Point position)
    {
        if (!TryNormalizePath(path, out var normalized) || !_items.TryGetValue(normalized, out var item))
        {
            return false;
        }

        return item.UpdatePosition(position);
    }

    public bool MarkActivity(string folderPath, DateTime activityTime)
    {
        var changed = false;
        foreach (var item in _items.Values.Where(item => IsPathUnder(folderPath, item.Path)))
        {
            changed |= item.MarkActivity(activityTime);
        }

        return changed;
    }

    public bool MarkOpened(string path, DateTime openedAt)
    {
        return TryNormalizePath(path, out var normalized)
            && _items.TryGetValue(normalized, out var item)
            && item.MarkOpened(openedAt);
    }

    public List<SavedPinnedFolder> ToSettings()
    {
        return Items.Select(item => new SavedPinnedFolder
        {
            Path = item.Path,
            Left = item.Position?.X,
            Top = item.Position?.Y,
            PinnedAt = item.PinnedAt,
            LastActivity = item.LastActivity,
            LastOpenedAt = item.LastOpenedAt
        }).ToList();
    }

    private static bool IsPathUnder(string path, string parent)
    {
        if (!TryNormalizePath(path, out var normalizedPath) || !TryNormalizePath(parent, out var normalizedParent))
        {
            return false;
        }

        return string.Equals(normalizedPath, normalizedParent, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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

public sealed class PinnedFolder
{
    public PinnedFolder(
        string path,
        DateTime pinnedAt,
        Point? position,
        DateTime lastActivity = default,
        DateTime lastOpenedAt = default)
    {
        Path = path;
        PinnedAt = pinnedAt;
        Position = position;
        LastActivity = lastActivity;
        LastOpenedAt = lastOpenedAt == default ? pinnedAt : lastOpenedAt;
    }

    public string Path { get; }
    public DateTime PinnedAt { get; }
    public Point? Position { get; private set; }
    public DateTime LastActivity { get; private set; }
    public DateTime LastOpenedAt { get; private set; }
    public bool HasUnreadActivity => LastActivity > LastOpenedAt;

    public string DisplayName
    {
        get
        {
            var trimmed = Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var name = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? Path : name;
        }
    }

    public bool UpdatePosition(Point position)
    {
        if (Position == position)
        {
            return false;
        }

        Position = position;
        return true;
    }

    public bool MarkActivity(DateTime activityTime)
    {
        if (activityTime <= LastActivity)
        {
            return false;
        }

        LastActivity = activityTime;
        return true;
    }

    public bool MarkOpened(DateTime openedAt)
    {
        if (openedAt <= LastOpenedAt)
        {
            return false;
        }

        LastOpenedAt = openedAt;
        return true;
    }
}
