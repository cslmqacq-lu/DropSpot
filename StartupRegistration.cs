using Microsoft.Win32;

namespace DropSpot;

internal static class StartupRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "DropSpot";

    public static bool TryApply(bool enabled, out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("无法打开 Windows 启动项注册表");
            if (enabled)
            {
                key.SetValue(
                    ValueName,
                    BuildCommand(Environment.ProcessPath ?? Application.ExecutablePath),
                    RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
            or IOException
            or InvalidOperationException
            or System.Security.SecurityException)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static string BuildCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{Path.GetFullPath(executablePath)}\" --startup";
    }
}
