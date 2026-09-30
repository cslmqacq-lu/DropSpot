using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace DropSpot;

/// <summary>管理员权限检测与“以管理员身份重启”。</summary>
internal static class Elevation
{
    /// <summary>重启时传给新进程，让它等待旧进程退出后再接管单实例锁。</summary>
    public const string WaitPreviousArgument = "--wait-previous";

    private static readonly Lazy<bool> IsElevatedLazy = new(() =>
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    });

    public static bool IsElevated => IsElevatedLazy.Value;

    public static bool IsAccessDenied(Exception exception)
    {
        return exception is UnauthorizedAccessException
            || exception is Win32Exception { NativeErrorCode: 5 };
    }

    /// <summary>以管理员身份启动 DropSpot 的另一个实例（会弹出一次 UAC 确认）。</summary>
    public static bool TryStartElevated(string arguments, out string? error)
    {
        error = null;
        var executable = Environment.ProcessPath ?? Application.ExecutablePath;
        try
        {
            Process.Start(new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            error = "已取消管理员授权";
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            error = ex.Message;
            AppLog.Warning($"以管理员身份启动失败：{ex.Message}");
            return false;
        }
    }
}
