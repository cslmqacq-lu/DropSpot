using System.Runtime.InteropServices;

namespace DropSpot;

/// <summary>统一的窗口外观（DWM 标题栏 / 圆角 / 边框），替代各窗体里重复的 P/Invoke 代码。</summary>
internal static class WindowChrome
{
    private const int DwmwaUseImmersiveDarkModeOld = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int CornerDoNotRound = 1;
    private const int CornerRound = 2;
    private const int ColorNone = unchecked((int)0xFFFFFFFE);

    /// <summary>深色标题栏：与 DropSpot 的深色界面保持一致（Windows 10 1809+，颜色需 Windows 11）。</summary>
    public static void ApplyDarkTitleBar(Form form)
    {
        if (!form.IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var handle = form.Handle;
        var enabled = 1;
        if (Set(handle, DwmwaUseImmersiveDarkMode, enabled) != 0)
        {
            _ = Set(handle, DwmwaUseImmersiveDarkModeOld, enabled);
        }

        _ = Set(handle, DwmwaCaptionColor, ColorTranslator.ToWin32(Color.Black));
        _ = Set(handle, DwmwaBorderColor, ColorTranslator.ToWin32(Theme.BorderStrong));
        _ = Set(handle, DwmwaTextColor, ColorTranslator.ToWin32(Color.White));
    }

    /// <summary>浮窗类窗口：去掉系统圆角和边框，外观完全由 PNG 外框决定。</summary>
    public static void RemoveSystemFrame(Form form)
    {
        if (!form.IsHandleCreated)
        {
            return;
        }

        _ = Set(form.Handle, DwmwaWindowCornerPreference, CornerDoNotRound);
        _ = Set(form.Handle, DwmwaBorderColor, ColorNone);
    }

    /// <summary>Windows 11 系统圆角。</summary>
    public static void RoundCorners(Form form)
    {
        if (form.IsHandleCreated)
        {
            _ = Set(form.Handle, DwmwaWindowCornerPreference, CornerRound);
        }
    }

    private static int Set(IntPtr handle, int attribute, int value)
    {
        return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
