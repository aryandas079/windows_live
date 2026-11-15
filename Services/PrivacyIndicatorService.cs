using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WindowsLive.Services;

/// <summary>
/// Monitors live Camera and Microphone usage across Windows 10/11
/// to surface iOS-style Green (Camera) and Orange (Microphone) privacy dots in the Dynamic Island notch.
/// </summary>
public class PrivacyIndicatorService
{
    public event Action<bool, bool>? PrivacyStateChanged;

    public bool IsCameraInUse { get; private set; } = false;
    public bool IsMicrophoneInUse { get; private set; } = false;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _pollTimer;

    private bool _simulatedCamera = false;
    private bool _simulatedMicrophone = false;
    private bool _wasapiMicActive = false;

    private static readonly string[] WebcamPaths =
    {
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam"
    };

    private static readonly string[] MicPaths =
    {
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone"
    };

    public PrivacyIndicatorService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _pollTimer.Tick += (s, e) => PollPrivacyStatus();
    }

    public void Start()
    {
        PollPrivacyStatus();
        _pollTimer.Start();
        App.Log("PrivacyIndicatorService started.");
    }

    public void Stop()
    {
        _pollTimer.Stop();
    }

    /// <summary>
    /// Update microphone active state directly from the WASAPI audio capture session monitor for instantaneous detection.
    /// </summary>
    public void SetWasapiMicState(bool isActive)
    {
        if (_wasapiMicActive != isActive)
        {
            _wasapiMicActive = isActive;
            PollPrivacyStatus();
        }
    }

    public void ToggleCameraSimulation()
    {
        _simulatedCamera = !_simulatedCamera;
        PollPrivacyStatus();
    }

    public void ToggleMicrophoneSimulation()
    {
        _simulatedMicrophone = !_simulatedMicrophone;
        PollPrivacyStatus();
    }

    private int _simMode = 0;
    public void CycleSimulation()
    {
        _simMode = (_simMode + 1) % 4;
        _simulatedCamera = (_simMode == 1 || _simMode == 3);
        _simulatedMicrophone = (_simMode == 2 || _simMode == 3);
        PollPrivacyStatus();
    }

    private void PollPrivacyStatus()
    {
        try
        {
            bool cameraProcessActive = IsCameraAppActive();
            bool cameraInUse = _simulatedCamera || cameraProcessActive || CheckRegistryStore(WebcamPaths);
            bool micInUse = _simulatedMicrophone || _wasapiMicActive || CheckRegistryStore(MicPaths);

            if (cameraInUse != IsCameraInUse || micInUse != IsMicrophoneInUse)
            {
                IsCameraInUse = cameraInUse;
                IsMicrophoneInUse = micInUse;

                _dispatcher.Invoke(() =>
                {
                    PrivacyStateChanged?.Invoke(IsCameraInUse, IsMicrophoneInUse);
                });
            }
        }
        catch (Exception ex)
        {
            App.Log($"PrivacyIndicatorService Poll error: {ex.Message}");
        }
    }

    private static bool IsCameraAppActive()
    {
        try
        {
            // Detect Windows Camera app process running in foreground
            var camProcs = Process.GetProcessesByName("WindowsCamera");
            if (camProcs.Length > 0) return true;
        }
        catch { }
        return false;
    }

    private static bool CheckRegistryStore(string[] paths)
    {
        var hives = new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32)
        };

        foreach (var (hive, view) in hives)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var subPath in paths)
                {
                    using var subKey = baseKey.OpenSubKey(subPath);
                    if (subKey != null && CheckKeyRecursive(subKey, 0))
                    {
                        return true;
                    }
                }
            }
            catch { }
        }
        return false;
    }

    private static bool CheckKeyRecursive(RegistryKey key, int depth)
    {
        if (key == null || depth > 3) return false;

        try
        {
            if (IsSessionActive(key)) return true;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var child = key.OpenSubKey(subKeyName);
                    if (child != null && CheckKeyRecursive(child, depth + 1))
                    {
                        return true;
                    }
                }
                catch { }
            }
        }
        catch { }

        return false;
    }

    private static bool IsSessionActive(RegistryKey key)
    {
        try
        {
            var valueNames = key.GetValueNames();
            if (valueNames.Contains("LastUsedTimeStart"))
            {
                object? startVal = key.GetValue("LastUsedTimeStart");
                object? stopVal = key.GetValue("LastUsedTimeStop");

                long start = 0;
                long stop = 0;

                if (startVal is long stl) start = stl;
                else if (startVal != null) long.TryParse(startVal.ToString(), out start);

                if (stopVal is long sl) stop = sl;
                else if (stopVal != null) long.TryParse(stopVal.ToString(), out stop);

                // If session started and has not stopped (stop == 0), or start is more recent than stop
                if (start > 0 && (stop == 0 || start > stop))
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }
}
