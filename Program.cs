namespace DropSpot;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        AppIcon.ApplyTaskbarIdentity();

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
    }
}
