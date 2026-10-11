using System;
using System.Threading;
using System.Windows;
namespace LanDrop.Desktop;
public partial class App : Application
{
    private Mutex? instance;
    public static bool SmokeTest { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SmokeTest = Array.Exists(e.Args, a => a == "--smoke-test");
        instance = new Mutex(true, "Local\\LanDrop.Desktop", out var first);
        if (!first && !SmokeTest) { MessageBox.Show("LAN Drop is already running. Check the notification area.", "LAN Drop"); Shutdown(); return; }
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
