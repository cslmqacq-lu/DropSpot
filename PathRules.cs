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
        "GPUCache", "Code Cache", "ShaderCache", "GrShaderCache", "DawnCache", "DawnGraphiteCache", "Crashpad"
    };

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
