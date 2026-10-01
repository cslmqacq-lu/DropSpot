using System.IO.Enumeration;

namespace DropSpot;

/// <summary>
/// 排除规则、临时文件与常见噪音目录的统一判断。
/// 规则分两类：
///   1. 完整路径（如 G:\Temp）：该目录及其所有子目录都会被排除；
///   2. 名称 / 通配符（如 node_modules、.git、*_temp_*）：路径中任意一级目录名匹配即排除，
///      也会匹配文件名（如 *.log）。
/// </summary>
internal static class PathRules
{
    private static readonly HashSet<string> TemporaryExtensions = new(
        new[] { ".tmp", ".temp", ".part", ".partial", ".crdownload", ".download", ".swp", ".swx" },
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SystemNoiseFileNames = new(
        new[] { "Thumbs.db", "desktop.ini", ".DS_Store" },
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] NoiseDirectoryNameList =
    {
        ".git", ".svn", ".hg",
        "node_modules", "__pycache__", ".pytest_cache", ".mypy_cache",
        ".vs", ".idea", ".gradle", ".next", ".nuxt", ".parcel-cache", ".turbo",
        "GPUCache", "Code Cache", "ShaderCache", "GrShaderCache", "DawnCache", "DawnGraphiteCache", "Crashpad",
        // AI 编程工具的工作目录
        ".claude", ".codex", ".cursor", ".superpowers", ".continue", ".windsurf", ".aider.tags.cache.v3",
        ".venv", "venv", ".ruff_cache", ".tox", ".nyc_output"
    };

    // ---------- 开发 / AI 编程预设：编译产物、缓存、日志、锁文件 ----------

    private static readonly string[] DevExtensionList =
    {
        ".pyc", ".pyo", ".class", ".o", ".obj", ".pdb", ".ilk", ".idb", ".ipch", ".tlog",
        ".cache", ".tsbuildinfo", ".map", ".log", ".lock", ".swp", ".swo", ".bak", ".orig", ".rej",
        ".suo", ".etl", ".dmp", ".db-journal", ".db-wal", ".db-shm", ".sqlite-journal"
    };

    private static readonly HashSet<string> DevFileNames = new(
        new[]
        {
            "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "bun.lockb", "Cargo.lock", "poetry.lock",
            "composer.lock", "Gemfile.lock", "packages.lock.json", "project.assets.json"
        },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>开发预设额外过滤的目录（构建中间产物）。</summary>
    private static readonly HashSet<string> DevDirectoryNames = new(new[] { "obj" }, StringComparer.OrdinalIgnoreCase);

    private static FileFilter _fileFilter = new(true, new HashSet<string>(DevExtensionList, StringComparer.OrdinalIgnoreCase));

    /// <summary>界面上展示用的开发预设扩展名。</summary>
    public static IReadOnlyList<string> DevExtensions => DevExtensionList;

    /// <summary>
    /// 设置扩展名过滤：<paramref name="filterDevFiles"/> 开启开发 / AI 编程预设，
    /// <paramref name="extraExtensions"/> 为用户额外隐藏的扩展名。界面进程和后台监视进程各自调用。
    /// </summary>
    public static void ConfigureFileFilter(bool filterDevFiles, IEnumerable<string>? extraExtensions)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (filterDevFiles)
        {
            extensions.UnionWith(DevExtensionList);
        }

        foreach (var extension in ParseExtensions(extraExtensions))
        {
            extensions.Add(extension);
        }

        _fileFilter = new FileFilter(filterDevFiles, extensions);
    }

    /// <summary>把 “log, .tmp  *.bak” 这类输入整理成 [".log", ".tmp", ".bak"]。</summary>
    public static IReadOnlyList<string> ParseExtensions(IEnumerable<string>? raw)
    {
        var result = new List<string>();
        foreach (var item in raw ?? Array.Empty<string>())
        {
            foreach (var part in (item ?? string.Empty).Split(new[] { ',', '，', ';', '；', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var extension = part.Trim().TrimStart('*');
                if (extension.Length == 0)
                {
                    continue;
                }

                if (!extension.StartsWith('.'))
                {
                    extension = "." + extension;
                }

                if (extension.Length > 1
                    && extension.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                    && !result.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(extension.ToLowerInvariant());
                }
            }
        }

        return result;
    }

    /// <summary>按扩展名 / 文件名规则应当隐藏的文件（开发产物、日志、锁文件、用户自定义扩展名）。</summary>
    public static bool IsFilteredFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var filter = _fileFilter;
        var name = Path.GetFileName(fileName);
        if (filter.DevPreset && DevFileNames.Contains(name))
        {
            return true;
        }

        var extension = Path.GetExtension(name);
        return extension.Length > 0 && filter.Extensions.Contains(extension);
    }

    /// <summary>开发预设开启时，路径中是否含有构建中间目录（obj）。</summary>
    public static bool ContainsDevDirectory(string? path)
    {
        if (!_fileFilter.DevPreset || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var segment in path.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (DevDirectoryNames.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>这个文件是否应该出现在“最近文件”里（临时文件和被过滤的扩展名都不显示）。</summary>
    public static bool IsHiddenFromRecent(string? filePath)
    {
        return IsTemporaryFileName(filePath)
            || IsFilteredFileName(filePath)
            || ContainsDevDirectory(filePath);
    }

    private sealed record FileFilter(bool DevPreset, HashSet<string> Extensions);

    private static readonly HashSet<string> NoiseDirectoryNames = new(NoiseDirectoryNameList, StringComparer.OrdinalIgnoreCase);

    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    /// <summary>界面上展示用的内置噪音目录名列表。</summary>
    public static IReadOnlyList<string> CommonNoiseDirectoryNames => NoiseDirectoryNameList;

    /// <summary>临时文件、Office 锁文件、系统缩略图等不应出现在列表中的文件名。</summary>
    public static bool IsTemporaryFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return true;
        }

        var name = Path.GetFileName(fileName);
        return name.StartsWith("~$", StringComparison.Ordinal)
            || name.StartsWith(".~lock.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith('~')
            || SystemNoiseFileNames.Contains(name)
            || TemporaryExtensions.Contains(Path.GetExtension(name));
    }

    /// <summary>路径中是否包含内置噪音目录（.git、node_modules、浏览器缓存等）。</summary>
    public static bool ContainsCommonNoiseDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var segment in path.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (NoiseDirectoryNames.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>不是完整路径、且不含路径分隔符的规则视为“名称规则”。</summary>
    public static bool IsNameRule(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return false;
        }

        var trimmed = rule.Trim();
        return trimmed.IndexOfAny(Separators) < 0 && !Path.IsPathFullyQualified(trimmed);
    }

    /// <summary>整理用户输入的规则：完整路径转成规范路径，名称规则去掉首尾空白和斜杠。</summary>
    public static string NormalizeRule(string rule)
    {
        var trimmed = rule.Trim().Trim('"');
        if (IsNameRule(trimmed))
        {
            return trimmed;
        }

        try
        {
            if (Path.IsPathFullyQualified(trimmed))
            {
                var full = Path.GetFullPath(trimmed);
                var root = Path.GetPathRoot(full);
                return string.Equals(root, full, StringComparison.OrdinalIgnoreCase)
                    ? full
                    : full.TrimEnd(Separators);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // 保留原样，交给匹配逻辑按前缀处理。
        }

        return trimmed.Trim(Separators);
    }

    /// <summary>判断路径是否命中某条排除规则。</summary>
    public static bool Matches(string? path, string? rule)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(rule))
        {
            return false;
        }

        var normalizedRule = NormalizeRule(rule);
        if (normalizedRule.Length == 0)
        {
            return false;
        }

        if (IsNameRule(normalizedRule))
        {
            var hasWildcard = normalizedRule.IndexOfAny(new[] { '*', '?' }) >= 0;
            foreach (var segment in path.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (hasWildcard
                        ? FileSystemName.MatchesSimpleExpression(normalizedRule, segment, ignoreCase: true)
                        : string.Equals(segment, normalizedRule, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        var fullPath = path;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // 按原始字符串比较。
        }

        fullPath = fullPath.TrimEnd(Separators);
        var prefix = normalizedRule.TrimEnd(Separators);
        return string.Equals(fullPath, prefix, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesAny(string? path, IEnumerable<string> rules)
    {
        foreach (var rule in rules)
        {
            if (Matches(path, rule))
            {
                return true;
            }
        }

        return false;
    }
}
