using System;
using System.IO;
using System.Windows;

namespace WindowsLive;

public partial class App : Application
{
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
            var win = new MainWindow();
            MainWindow = win;
            Log("Calling win.Show()...");
            win.Show();
            Log("win.Show() completed successfully!");
        }
        catch (Exception ex)
        {
            Log($"Exception in OnStartup: {ex}");
            throw;
        }
    }
}
