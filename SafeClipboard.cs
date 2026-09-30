using System.Collections.Specialized;
using System.Runtime.InteropServices;

namespace DropSpot;

/// <summary>
/// 剪贴板经常被其他程序短暂占用，直接调用 Clipboard.SetText 会抛 ExternalException。
/// 这里统一做短暂重试，并把失败转换成可显示的错误文本。
/// </summary>
internal static class SafeClipboard
{
    private const int Attempts = 4;
    private const int RetryDelayMs = 60;

    public static bool TrySetText(string? text, out string? error)
    {
        if (string.IsNullOrEmpty(text))
        {
            error = "没有可复制的内容";
            return false;
        }

        return TryRun(() => Clipboard.SetText(text), out error);
    }

    /// <summary>把文件本身放到剪贴板，之后可在资源管理器或聊天软件里直接粘贴。</summary>
    public static bool TrySetFiles(IEnumerable<string> filePaths, out string? error)
    {
        var list = new StringCollection();
        foreach (var path in filePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            list.Add(path);
        }

        if (list.Count == 0)
        {
            error = "没有可复制的文件";
            return false;
        }

        return TryRun(() => Clipboard.SetFileDropList(list), out error);
    }

    private static bool TryRun(Action action, out string? error)
    {
        error = null;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex) when (ex is ExternalException or ThreadStateException)
            {
                if (attempt == Attempts)
                {
                    error = "剪贴板正被其他程序占用，请稍后重试";
                    AppLog.Warning($"写入剪贴板失败：{ex.Message}");
                    return false;
                }

                Thread.Sleep(RetryDelayMs);
            }
        }

        return false;
    }
}
