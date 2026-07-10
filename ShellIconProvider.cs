using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace DiskWriteWatcher;

public static class ShellIconProvider
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;

    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Image FolderIcon()
    {
        return Cache.GetOrAdd("__folder_large__", _ => LoadIcon("folder", FileAttributeDirectory, large: true));
    }

    public static Image FileIcon(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = "__file__";
        }

        return Cache.GetOrAdd(extension, _ => LoadIcon("file" + extension, FileAttributeNormal, large: false));
    }

    public static void DisposeCache()
    {
        foreach (var pair in Cache.ToArray())
        {
            if (Cache.TryRemove(pair.Key, out var bitmap))
            {
                bitmap.Dispose();
            }
        }
    }

    private static Bitmap LoadIcon(string path, uint attributes, bool large)
    {
        var info = new ShFileInfo();
        var result = SHGetFileInfo(
            path,
            attributes,
            ref info,
            (uint)Marshal.SizeOf<ShFileInfo>(),
            ShgfiIcon | (large ? ShgfiLargeIcon : ShgfiSmallIcon) | ShgfiUseFileAttributes);

        if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero)
        {
            return new Bitmap(16, 16);
        }

        try
        {
            using var icon = Icon.FromHandle(info.IconHandle);
            return icon.ToBitmap();
        }
        finally
        {
            DestroyIcon(info.IconHandle);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref ShFileInfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }
}
