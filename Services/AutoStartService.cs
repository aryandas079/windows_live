using System;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;

namespace WindowsLive.Services;

/// <summary>
/// Ensures WindowsLive automatically launches on system boot / restart / login
/// and stays registered across restarts and shutdowns.
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WindowsLive";

    public static void EnsureAutoStartRegistered()
    {
        try
        {
            // Determine executable path. If launched via 'dotnet WindowsLive.dll', resolve to WindowsLive.exe
            string candidateExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WindowsLive.exe");
            string exePath;
            if (File.Exists(candidateExe))
            {
                exePath = candidateExe;
            }
            else
            {
                exePath = Environment.ProcessPath ?? (Process.GetCurrentProcess().MainModule?.FileName ?? candidateExe);
            }

            if (!File.Exists(exePath)) return;

            // 1. Register in HKCU\Software\Microsoft\Windows\CurrentVersion\Run
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key != null)
                {
                    string quotedPath = $"\"{exePath}\"";
                    var existingVal = key.GetValue(AppName) as string;
                    if (existingVal != quotedPath)
                    {
                        key.SetValue(AppName, quotedPath);
                        App.Log($"AutoStartService: Registered in HKCU Run: {quotedPath}");
                    }
                }
            }

            // 2. Register Windows Startup Folder Shortcut (.lnk)
            EnsureStartupShortcut(exePath);
        }
        catch (Exception ex)
        {
            App.Log($"AutoStartService error: {ex.Message}");
        }
    }

    private static void EnsureStartupShortcut(string exePath)
    {
        try
        {
            string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (!Directory.Exists(startupFolder)) return;

            string shortcutPath = Path.Combine(startupFolder, "WindowsLive.lnk");

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            shortcut.Description = "Windows Dynamic Island Dock";
            shortcut.Save();
            App.Log($"AutoStartService: Verified Startup shortcut at '{shortcutPath}'");
        }
        catch (Exception ex)
        {
            App.Log($"AutoStartService EnsureStartupShortcut error: {ex.Message}");
        }
    }
}
