namespace DiskWriteWatcher;

public sealed class WatchScope
{
    public WatchScope(string path, bool enabled = true)
    {
        Path = NormalizePath(path);
        Enabled = enabled;
    }

    public string Path { get; }
    public bool Enabled { get; set; }

    public string Kind
    {
        get
        {
            var root = System.IO.Path.GetPathRoot(Path);
            return string.Equals(root, Path, StringComparison.OrdinalIgnoreCase) ? "硬盘" : "文件夹";
        }
    }

    public override string ToString() => Path;

    private static string NormalizePath(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var root = System.IO.Path.GetPathRoot(fullPath) ?? fullPath;
        if (string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        return fullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }
}
