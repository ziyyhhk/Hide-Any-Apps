using System;
using Microsoft.UI.Xaml;
using HideAnyWindowManager.Services;
using HideAnyWindowManager.Util;

namespace HideAnyWindowManager;

public partial class App : Application
{
    public static ConfigStore ConfigStore { get; } = new();
    public static ServiceController ServiceController { get; } = new();
    public static ProcessEnumerator ProcessEnumerator { get; } = new();
    public static AutostartManager AutostartManager { get; } = new();

    private Window? _mainWindow;

    public App() { InitializeComponent(); }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cli = Environment.GetCommandLineArgs();
        for (int i = 1; i < cli.Length; i++)
        {
            if (string.Equals(cli[i], AutostartManager.StartServiceArg, StringComparison.OrdinalIgnoreCase))
            {
                // Logon-trigger path: start the service and exit without UI.
                // Skip the launch entirely if the user last pressed Stop — otherwise every
                // logon resurrects a service they explicitly stopped.
                var cfg = ConfigStore.LoadAsync().GetAwaiter().GetResult();
                if (ServiceController.ShouldStartAtLogon(cfg))
                    ServiceController.TryStartService();
                else
                    ManagerLog.Write("--start-service: serviceState=stopped, not launching");
                Environment.Exit(0);
                return;
            }

            if (string.Equals(cli[i], AutostartManager.StopServiceArg, StringComparison.OrdinalIgnoreCase))
            {
                // Uninstall helper: ask the service to restore every hidden window and
                // exit before the uninstaller deletes its exe. The service polls config
                // at ~1 Hz, so give it a few seconds to release the mutex.
                try
                {
                    var cfg = ConfigStore.LoadAsync().GetAwaiter().GetResult();
                    cfg.ServiceState = "stopped";
                    ConfigStore.SaveImmediateAsync(cfg).GetAwaiter().GetResult();
                    for (int wait = 0; wait < 50 && ServiceController.IsServiceRunning(); wait++)
                        System.Threading.Thread.Sleep(100);
                }
                catch { /* best effort — uninstall must continue */ }
                Environment.Exit(0);
                return;
            }
        }

        _mainWindow = new MainWindow();
        _mainWindow.Activate();

        // If autostart was enabled in an older build, re-register it using the
        // current per-user Run entry (the old build used a Task Scheduler task).
        try { if (AutostartManager.IsEnabled()) AutostartManager.TryEnable(); }
        catch { /* non-fatal */ }
    }
}
