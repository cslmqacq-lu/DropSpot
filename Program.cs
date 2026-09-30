namespace DropSpot;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        AppIcon.ApplyTaskbarIdentity();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) =>
        {
            AppLog.Error("UI 线程发生未处理异常", eventArgs.Exception);
            MessageBox.Show("DropSpot 遇到异常，诊断信息已写入日志。", "DropSpot", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            AppLog.Error("进程发生未处理异常", eventArgs.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            AppLog.Error("后台任务发生未观察异常", eventArgs.Exception);
            eventArgs.SetObserved();
        };
        AppLog.Info($"DropSpot v{Application.ProductVersion} 启动（{(Elevation.IsElevated ? "管理员" : "普通")}权限）");

        if (args.Any(arg => string.Equals(arg, "--ui-smoke-test", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = SmokeTest.RunUiOnly();
            return;
        }

        if (args.Any(arg => string.Equals(arg, "--smoke-test", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = SmokeTest.Run();
            return;
        }

        var startMinimized = args.Any(arg =>
            string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));
        var waitForPrevious = args.Any(arg =>
            string.Equals(arg, Elevation.WaitPreviousArgument, StringComparison.OrdinalIgnoreCase));

        using var instance = AcquireInstance(waitForPrevious);
        if (!instance.IsFirstInstance)
        {
            if (instance.OtherInstanceIsElevated)
            {
                // 普通权限无法唤醒管理员实例；开机自启重复启动时静默退出，手动启动时给出提示。
                if (!startMinimized)
                {
                    MessageBox.Show(
                        "DropSpot 已经以管理员身份在运行，请通过系统托盘图标或悬浮窗打开。",
                        "DropSpot",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            instance.SignalActivation();
            return;
        }

        using var mainForm = new MainForm(startMinimized);
        instance.StartListening(() =>
        {
            if (!mainForm.IsDisposed && mainForm.IsHandleCreated)
            {
                mainForm.BeginInvoke(mainForm.ActivateFromExternalRequest);
            }
        });
        Application.Run(mainForm);
        AppLog.Info("DropSpot 正常退出");
    }

    /// <summary>
    /// 获取单实例锁。以管理员身份重启时，旧进程可能还没完全退出，这里最多等待 10 秒。
    /// </summary>
    private static SingleInstanceCoordinator AcquireInstance(bool waitForPrevious)
    {
        var deadline = DateTime.UtcNow + (waitForPrevious ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
        while (true)
        {
            var instance = new SingleInstanceCoordinator();
            if (instance.IsFirstInstance || DateTime.UtcNow >= deadline)
            {
                return instance;
            }

            instance.Dispose();
            Thread.Sleep(250);
        }
    }
}
