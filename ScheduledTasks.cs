using System.Diagnostics;
using System.Security;

namespace DropSpot;

/// <summary>对 schtasks.exe 的最小封装。</summary>
internal static class ScheduledTasks
{
    public static bool Exists(string taskName)
    {
        return Run($"/Query /TN \"{taskName}\"", out _) == 0;
    }

    /// <summary>按需运行任务。任务若是“以最高权限运行”，普通权限调用也不会弹 UAC。</summary>
    public static bool Start(string taskName, out string? error)
    {
        var exitCode = Run($"/Run /TN \"{taskName}\"", out var output);
        error = exitCode == 0 ? null : output;
        return exitCode == 0;
    }

    public static bool Delete(string taskName, out string? error)
    {
        var exitCode = Run($"/Delete /TN \"{taskName}\" /F", out var output);
        error = exitCode == 0 ? null : output;
        return exitCode == 0;
    }

    /// <summary>用 XML 注册（覆盖）任务。需要管理员权限才能注册“最高权限”任务。</summary>
    public static bool Register(string taskName, string xml, out string? error)
    {
        error = null;
        var xmlPath = Path.Combine(Path.GetTempPath(), $"DropSpot-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);
            var exitCode = Run($"/Create /TN \"{taskName}\" /XML \"{xmlPath}\" /F", out var output);
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

    /// <summary>
    /// 生成以当前用户、最高权限、交互式令牌运行的任务 XML。
    /// ExecutionTimeLimit=PT0S：不限制运行时长（默认 72 小时会把常驻进程杀掉）。
    /// Priority=5：普通优先级（任务计划默认 7 为低于正常）。
    /// </summary>
    internal static string BuildElevatedTaskXml(
        string executablePath,
        string userId,
        string arguments,
        string description,
        bool runAtLogon)
    {
        var fullPath = Path.GetFullPath(executablePath);
        var command = SecurityElement.Escape(fullPath);
        var workingDirectory = SecurityElement.Escape(Path.GetDirectoryName(fullPath) ?? string.Empty);
        var user = SecurityElement.Escape(userId);
        var args = SecurityElement.Escape(arguments);
        var text = SecurityElement.Escape(description);
        var triggers = runAtLogon
            ? $"""
                <Triggers>
                  <LogonTrigger>
                    <Enabled>true</Enabled>
                    <UserId>{user}</UserId>
                    <Delay>PT5S</Delay>
                  </LogonTrigger>
                </Triggers>
              """
            : "  <Triggers />";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>{text}</Description>
              </RegistrationInfo>
            {triggers}
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
                  <Arguments>{args}</Arguments>
                  <WorkingDirectory>{workingDirectory}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static int Run(string arguments, out string output)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("schtasks.exe", arguments)
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
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            output = ex.Message;
            return -1;
        }
    }
}
