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
        AppLog.Info($"DropSpot v{Application.ProductVersion} 启动");

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

        using var instance = new SingleInstanceCoordinator();
        if (!instance.IsFirstInstance)
        {
            instance.SignalActivation();
            return;
        }

        var startMinimized = args.Any(arg =>
            string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));
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
}
