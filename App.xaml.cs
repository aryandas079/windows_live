using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace WindowsLive;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_debug.log");

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("App.OnStartup called");

        const string mutexName = "Local\\WindowsLive_DynamicIsland_SingletonMutex";
        try
        {
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
            if (!createdNew)
            {
                Log("Another instance is already running (mutex locked); exiting.");
                Shutdown();
                return;
            }
        }
        catch (Exception ex)
        {
            Log($"Mutex creation warning: {ex.Message}");
        }

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log($"AppDomain UnhandledException: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log($"DispatcherUnhandledException: {args.Exception}");
            args.Handled = false;
        };

        try
        {
            base.OnStartup(e);
            Log("Creating MainWindow...");
            var ownerWindow = new Window
            {
                Width = 0,
                Height = 0,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Visibility = Visibility.Collapsed
            };
            var ownerHelper = new System.Windows.Interop.WindowInteropHelper(ownerWindow);
            ownerHelper.EnsureHandle();
            Services.WindowHelper.MakeToolWindow(ownerWindow);

            var win = new MainWindow
            {
                Owner = ownerWindow
            };
            MainWindow = win;
            Log("Calling win.Show()...");
            win.Show();
            Services.WindowHelper.MakeToolWindow(win);
            Log("win.Show() completed successfully!");
        }
        catch (Exception ex)
        {
            Log($"Exception in OnStartup: {ex}");
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
