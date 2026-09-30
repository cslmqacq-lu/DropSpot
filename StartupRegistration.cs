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
                var currentExecutable = Environment.ProcessPath ?? Application.ExecutablePath;
                var existingCommand = key.GetValue(ValueName) as string;
                var targetExecutable = ResolveStartupExecutable(
                    currentExecutable,
                    existingCommand,
                    InstalledExecutablePath);
                if (targetExecutable is null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
                else
                {
                    var command = BuildCommand(targetExecutable);
                    if (!string.Equals(existingCommand, command, StringComparison.OrdinalIgnoreCase))
                    {
                        key.SetValue(ValueName, command, RegistryValueKind.String);
                    }
                }
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

    internal static string? ResolveStartupExecutable(
        string currentExecutable,
        string? existingCommand,
        string installedExecutable)
    {
        var currentPath = Path.GetFullPath(currentExecutable);
        if (!IsDevelopmentExecutable(currentPath))
        {
            return currentPath;
        }

        var existingPath = ExtractExecutablePath(existingCommand);
        if (!string.IsNullOrWhiteSpace(existingPath)
            && !IsDevelopmentExecutable(existingPath)
            && File.Exists(existingPath))
        {
            return existingPath;
        }

        var installedPath = Path.GetFullPath(installedExecutable);
        return File.Exists(installedPath) ? installedPath : null;
    }

    internal static bool IsDevelopmentExecutable(string executablePath)
    {
        var path = Path.GetFullPath(executablePath).Replace('/', '\\');
        return path.Contains("\\bin\\Debug\\", StringComparison.OrdinalIgnoreCase)
            || path.Contains("\\bin\\Release\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            return closingQuote > 1 ? trimmed[1..closingQuote] : null;
        }

        var separator = trimmed.IndexOf(' ');
        return separator > 0 ? trimmed[..separator] : trimmed;
    }

    private static string InstalledExecutablePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "DropSpot",
            "DropSpot.exe");
}
