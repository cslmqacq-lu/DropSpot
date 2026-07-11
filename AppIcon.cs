using System.Reflection;
using System.Runtime.InteropServices;

namespace DropSpot;

internal static class AppIcon
{
    private const string ResourceName = "DropSpot.AppIcon.ico";
    private const string AppUserModelId = "cslm.DropSpot";

    public static Icon Create()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("找不到 DropSpot 图标资源");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    public static void ApplyTaskbarIdentity()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
