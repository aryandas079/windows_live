using System;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WindowsLive.Services;

/// <summary>
/// Monitors Windows Night Light (Blue Light Reduction) and Dark Mode state.
/// Fires NightModeChanged when toggled on or off.
/// </summary>
public class NightLightWatcherService
{
    public event Action<bool>? NightModeChanged;

    private readonly DispatcherTimer _pollTimer;
    private bool _lastNightLightState = false;
    private bool _initialized = false;

    public bool IsNightModeActive => _lastNightLightState;

    public NightLightWatcherService()
    {
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000)
        };
        _pollTimer.Tick += (s, e) => CheckState();

        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            CheckState();
        };
    }

    public void Start()
    {
        CheckState(initial: true);
        _pollTimer.Start();
    }

    public void Stop()
    {
        _pollTimer.Stop();
    }

    /// <summary>
    /// Checks the Windows registry for Night Light state.
    /// In Windows 10 & 11, bluelightreductionstate binary data at byte 18 indicates:
    /// 0x15 = Enabled/Active, 0x13 or 0x14 = Disabled/Off.
    /// </summary>
    private void CheckState(bool initial = false)
    {
        try
        {
            bool isNightLightOn = ReadNightLightStateFromRegistry();

            if (!_initialized)
            {
                _initialized = true;
                _lastNightLightState = isNightLightOn;
                App.Log($"NightLightWatcherService initialized. Initial state: {(isNightLightOn ? "ON" : "OFF")}");
                return;
            }

            if (isNightLightOn != _lastNightLightState)
            {
                _lastNightLightState = isNightLightOn;
                App.Log($"NightLightWatcherService state changed to: {(isNightLightOn ? "ON" : "OFF")}");
                NightModeChanged?.Invoke(isNightLightOn);
            }
        }
        catch (Exception ex)
        {
            App.Log($"NightLightWatcherService CheckState error: {ex.Message}");
        }
    }

    private bool ReadNightLightStateFromRegistry()
    {
        try
        {
            const string basePath = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current";
            using var rootKey = Registry.CurrentUser.OpenSubKey(basePath);
            if (rootKey == null) return false;

            // Enumerate subkeys looking for bluelightreductionstate
            var subKeyNames = rootKey.GetSubKeyNames();
            foreach (var subName in subKeyNames)
            {
                if (!subName.Contains("bluelightreductionstate", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var subKey = rootKey.OpenSubKey(subName);
                if (subKey == null) continue;

                // 1. Try reading "Data" from direct key
                byte[]? data = subKey.GetValue("Data") as byte[];

                // 2. If null, check nested child keys (e.g. windows.data.bluelightreduction.bluelightreductionstate)
                if (data == null)
                {
                    foreach (var childName in subKey.GetSubKeyNames())
                    {
                        using var childKey = subKey.OpenSubKey(childName);
                        if (childKey != null)
                        {
                            data = childKey.GetValue("Data") as byte[];
                            if (data != null && data.Length > 0)
                                break;
                        }
                    }
                }

                if (data != null && data.Length > 18)
                {
                    // Byte 18 is 0x15 when Night Light is turned on/active
                    if (data[18] == 0x15)
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"ReadNightLightStateFromRegistry error: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Opens the Windows Night Light settings panel.
    /// </summary>
    public void OpenNightLightSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:nightlight",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            App.Log($"Failed to open nightlight settings: {ex.Message}");
        }
    }

    /// <summary>
    /// For manual testing / debugging or keyboard shortcuts: toggles the perceived state and triggers events.
    /// </summary>
    public void SimulateToggle()
    {
        _lastNightLightState = !_lastNightLightState;
        NightModeChanged?.Invoke(_lastNightLightState);
    }
}
