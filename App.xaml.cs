using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace WindowsLive;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_debug.log");

    private static readonly object _logLock = new();
    public static void Log(string message)
    {
        try
        {
            lock (_logLock)
            {
                using var fs = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var sw = new StreamWriter(fs);
                sw.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            }
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
                var currentPid = Environment.ProcessId;
                var existingProcesses = System.Diagnostics.Process.GetProcessesByName("WindowsLive");
                bool reallyRunning = false;
                foreach (var p in existingProcesses)
                {
                    if (p.Id != currentPid)
                    {
                        reallyRunning = true;
                        break;
                    }
                }

                if (reallyRunning)
                {
                    Log("Another instance is already running; exiting.");
                    Shutdown();
                    return;
                }
                else
                {
                    Log("Stale mutex detected from killed process; continuing.");
                }
            }
        }
        catch (AbandonedMutexException)
        {
            Log("Mutex was abandoned by previously terminated process; continuing.");
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
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Log("Creating MainWindow...");

            var win = new MainWindow();
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
