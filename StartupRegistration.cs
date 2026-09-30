using Microsoft.Win32;

namespace DropSpot;

internal static class StartupRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "DropSpot";

    /// <summary>1.0.7 开发期间曾把界面注册成“最高权限”开机任务，会导致拖放失效，需要清理。</summary>
    internal const string LegacyTaskName = "DropSpot";

    /// <summary>
    /// 应用开机启动设置：界面始终以普通权限通过注册表 Run 启动项启动
    /// （拖放等交互需要普通权限），磁盘监视交给已授权的后台监视进程。
    /// 返回 false 表示操作失败；返回 true 时 error 仍可能带有需要告诉用户的提示。
    /// </summary>
    public static bool TryApply(bool enabled, out string? error)
    {
        error = null;
        try
        {
            RemoveLegacyElevatedTask(ref error);

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("无法打开 Windows 启动项注册表");
            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var currentExecutable = Environment.ProcessPath ?? Application.ExecutablePath;
            var existingCommand = key.GetValue(ValueName) as string;
            var targetExecutable = ResolveStartupExecutable(
                currentExecutable,
                existingCommand,
                InstalledExecutablePath);
            if (targetExecutable is null)
            {
                // 开发版且找不到稳定的安装位置：不注册，避免开机启动指向 bin 目录。
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var command = BuildCommand(targetExecutable);
            if (!string.Equals(existingCommand, command, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
            or IOException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception
            or System.Security.SecurityException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>删除旧版的“以最高权限运行界面”开机任务；普通权限删不掉时交给后台监视进程处理。</summary>
    internal static void RemoveLegacyElevatedTask(ref string? notice)
    {
        if (!ScheduledTasks.Exists(LegacyTaskName))
        {
            return;
        }

        if (ScheduledTasks.Delete(LegacyTaskName, out var deleteError))
        {
            AppLog.Info("已移除旧版管理员开机任务");
            return;
        }

        AppLog.Warning($"旧版管理员开机任务暂时无法移除：{deleteError}");
        notice = "检测到旧版的管理员开机任务，授权后台监视时会自动清理";
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
