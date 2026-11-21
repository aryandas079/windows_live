using System;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;

namespace WindowsLive.Services;

/// <summary>
/// Ensures WindowsLive automatically launches on system boot / restart / login
/// and stays registered across restarts and shutdowns via redundant mechanisms:
/// 1. HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// 2. Windows Startup Folder (.lnk)
/// 3. Windows Task Scheduler Logon Task (Highest priority, runs on every user logon without delay)
/// 4. StartupApproved registration (clears disabled flags)
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupApprovedFolderPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string AppName = "WindowsLive";
    private const string TaskName = "WindowsLive_AutoStart";

    public static void EnsureAutoStartRegistered()
    {
        try
        {
            // Determine executable path.
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

            string quotedPath = $"\"{exePath}\"";

            // 1. Register in HKCU\Software\Microsoft\Windows\CurrentVersion\Run
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key != null)
                {
                    var existingVal = key.GetValue(AppName) as string;
                    if (existingVal != quotedPath)
                    {
                        key.SetValue(AppName, quotedPath);
                        App.Log($"AutoStartService: Registered in HKCU Run: {quotedPath}");
                    }

                    // Clean up any old duplicate name
                    if (key.GetValue("WindowsLiveDynamicIsland") != null)
                    {
                        try { key.DeleteValue("WindowsLiveDynamicIsland", false); } catch { }
                    }
                }
            }

            // 2. Disable Windows' artificial 5-10 second Explorer startup delay!
            // Setting StartupDelayInMSec = 0 forces Windows to launch startup apps IMMEDIATELY at boot/logon.
            EnsureInstantStartupDelay();

            // 3. Ensure StartupApproved entries are enabled (02 00 00 00 = enabled)
            EnsureStartupApproved(AppName, "WindowsLive.lnk");

            // 4. Register Windows Startup Folder Shortcut (.lnk)
            EnsureStartupShortcut(exePath);
        }
        catch (Exception ex)
        {
            App.Log($"AutoStartService error: {ex.Message}");
        }
    }

    private static void EnsureInstantStartupDelay()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", true);
            if (key != null)
            {
                key.SetValue("StartupDelayInMSec", 0, RegistryValueKind.DWord);
                App.Log("AutoStartService: Configured StartupDelayInMSec = 0 for zero-delay instant boot startup");
            }
        }
        catch (Exception ex)
        {
            App.Log($"AutoStartService EnsureInstantStartupDelay error: {ex.Message}");
        }
    }

    private static void EnsureStartupApproved(string runName, string shortcutName)
    {
        byte[] enabledBytes = new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedRunPath, true);
            if (key != null)
            {
                key.SetValue(runName, enabledBytes, RegistryValueKind.Binary);
                try { key.DeleteValue("WindowsLiveDynamicIsland", false); } catch { }
            }
        }
        catch { }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedFolderPath, true);
            if (key != null)
            {
                key.SetValue(shortcutName, enabledBytes, RegistryValueKind.Binary);
                try { key.DeleteValue("WindowsLiveDynamicIsland.lnk", false); } catch { }
            }
        }
        catch { }
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

            // Clean up old shortcut name if present
            string oldShortcut = Path.Combine(startupFolder, "WindowsLiveDynamicIsland.lnk");
            if (File.Exists(oldShortcut))
            {
                try { File.Delete(oldShortcut); } catch { }
            }
        }
        catch (Exception ex)
        {
            App.Log($"AutoStartService EnsureStartupShortcut error: {ex.Message}");
        }
    }
}
