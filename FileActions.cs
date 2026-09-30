using System.Diagnostics;

namespace DropSpot;

/// <summary>与资源管理器交互的文件操作。所有方法都可以在后台线程调用。</summary>
internal static class FileActions
{
    /// <summary>
    /// 打开文件所在文件夹并选中该文件。文件不存在时返回 false，由调用方决定是否退回普通打开。
    /// </summary>
    public static bool TryRevealInExplorer(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
        {
            UseShellExecute = true
        });
        return true;
    }

    /// <summary>弹出系统的“打开方式”对话框。</summary>
    public static bool TryOpenWith(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {filePath}")
        {
            UseShellExecute = false
        });
        return true;
    }

    /// <summary>
    /// 从一组最近记录里挑出最新且仍然存在的普通文件（跳过临时文件）。
    /// </summary>
    public static string? LatestExistingFile(IReadOnlyList<ChangeRecord> records)
    {
        return LatestFileSelector.SelectLatestExisting(records, File.Exists)?.FilePath;
    }

    /// <summary>构造拖拽数据：标准 Windows FileDrop，可拖到资源管理器、聊天软件、浏览器上传框。</summary>
    public static DataObject? CreateFileDropData(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        return new DataObject(DataFormats.FileDrop, new[] { filePath });
    }
}
