namespace DiskWriteWatcher;

public sealed class PendingRecordBuffer
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ChangeRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _capacity;

    public PendingRecordBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _records.Count;
            }
        }
    }

    public void Add(ChangeRecord record)
    {
        var key = NormalizeKey(record.FilePath);
        lock (_sync)
        {
            if (_records.TryGetValue(key, out var current))
            {
                if (record.Time >= current.Time)
                {
                    _records[key] = record;
                }

                return;
            }

            if (_records.Count >= _capacity)
            {
                var oldest = _records.MinBy(pair => pair.Value.Time);
                _records.Remove(oldest.Key);
            }

            _records.Add(key, record);
        }
    }

    public IReadOnlyList<ChangeRecord> DrainLatest(int maxCount)
    {
        if (maxCount <= 0)
        {
            return Array.Empty<ChangeRecord>();
        }

        lock (_sync)
        {
            if (_records.Count == 0)
            {
                return Array.Empty<ChangeRecord>();
            }

            var snapshot = _records.Values
                .OrderByDescending(record => record.Time)
                .Take(maxCount)
                .OrderBy(record => record.Time)
                .ToArray();
            _records.Clear();
            return snapshot;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _records.Clear();
        }
    }

    private static string NormalizeKey(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
