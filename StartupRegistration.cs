using Microsoft.Win32;

namespace DropSpot;

internal static class StartupRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "DropSpot";

    internal const string TaskName = "DropSpot";

    /// <summary>
    /// 应用开机启动设置。
    /// USN 监视需要管理员权限，而注册表 Run 启动项永远不会提权，所以：
    ///   - 管理员身份运行时：注册“以最高权限运行”的计划任务，并移除 Run 启动项；
    ///   - 普通身份运行时：已有计划任务则保持不动；否则退回 Run 启动项，并通过 error 给出提示。
    /// 返回 false 表示操作失败；返回 true 时 error 仍可能带有需要告诉用户的提示。
    /// </summary>
    public static bool TryApply(bool enabled, out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("无法打开 Windows 启动项注册表");
            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                if (ScheduledTaskExists() && !TryDeleteScheduledTask(out var deleteError))
                {
                    error = Elevation.IsElevated
                        ? $"移除开机任务失败：{deleteError}"
                        : "开机任务由管理员身份创建，需要以管理员身份运行 DropSpot 后再关闭此项";
                    return false;
                }

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

            if (Elevation.IsElevated)
            {
                if (TryRegisterScheduledTask(targetExecutable, out var taskError))
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                    return true;
                }

                AppLog.Warning($"注册管理员开机任务失败，改用普通启动项：{taskError}");
                error = $"管理员开机任务注册失败，已改用普通启动项：{taskError}";
                SetRunValue(key, existingCommand, targetExecutable);
                return true;
            }

            if (ScheduledTaskExists())
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            SetRunValue(key, existingCommand, targetExecutable);
            error = "已设置普通开机启动，但监视磁盘需要管理员权限。请点击“以管理员重启”一次，DropSpot 会改为以管理员身份开机启动";
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

    private static void SetRunValue(RegistryKey key, string? existingCommand, string targetExecutable)
    {
        var command = BuildCommand(targetExecutable);
        if (!string.Equals(existingCommand, command, StringComparison.OrdinalIgnoreCase))
        {
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
    }

    internal static bool ScheduledTaskExists()
    {
        return RunSchTasks($"/Query /TN \"{TaskName}\"", out _) == 0;
    }

    private static bool TryDeleteScheduledTask(out string? error)
    {
        var exitCode = RunSchTasks($"/Delete /TN \"{TaskName}\" /F", out var output);
        error = exitCode == 0 ? null : output;
        return exitCode == 0;
    }

    private static bool TryRegisterScheduledTask(string executablePath, out string? error)
    {
        error = null;
        var xmlPath = Path.Combine(Path.GetTempPath(), $"DropSpot-task-{Guid.NewGuid():N}.xml");
        try
        {
            string userId;
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                userId = identity.Name;
            }

            File.WriteAllText(xmlPath, BuildTaskXml(executablePath, userId), System.Text.Encoding.Unicode);
            var exitCode = RunSchTasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F", out var output);
            if (exitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(output) ? $"schtasks 退出码 {exitCode}" : output;
                return false;
            }

            return true;
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal static string BuildTaskXml(string executablePath, string userId)
    {
        var fullPath = Path.GetFullPath(executablePath);
        var command = System.Security.SecurityElement.Escape(fullPath);
        var workingDirectory = System.Security.SecurityElement.Escape(Path.GetDirectoryName(fullPath) ?? string.Empty);
        var user = System.Security.SecurityElement.Escape(userId);
        // ExecutionTimeLimit=PT0S：不限制运行时长（默认 72 小时会把常驻程序杀掉）。
        // Priority=5：普通优先级（任务计划默认 7 为低于正常）。
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>DropSpot 开机启动（以最高权限运行，用于读取磁盘 USN 日志）</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT5S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{command}</Command>
                  <Arguments>--startup</Arguments>
                  <WorkingDirectory>{workingDirectory}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static int RunSchTasks(string arguments, out string output)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000))
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
            }

            output = "schtasks 超时";
            return -1;
        }

        output = (stderr.Result + " " + stdout.Result).Trim();
        return process.ExitCode;
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
