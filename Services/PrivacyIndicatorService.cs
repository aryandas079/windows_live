using System;
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
            Interval = TimeSpan.FromMilliseconds(400)
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
            bool cameraInUse = _simulatedCamera || CheckRegistryStore(WebcamPaths);
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

    private static bool CheckRegistryStore(string[] paths)
    {
        foreach (var subPath in paths)
        {
            if (CheckRootKey(Registry.CurrentUser, subPath)) return true;
            if (CheckRootKey(Registry.LocalMachine, subPath)) return true;
        }
        return false;
    }

    private static bool CheckRootKey(RegistryKey root, string subPath)
    {
        try
        {
            using var key = root.OpenSubKey(subPath);
            if (key == null) return false;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    if (IsSessionActive(subKey)) return true;

                    // Check NonPackaged subkeys (standard Win32 apps like Chrome, Zoom, OBS)
                    if (subKeyName.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var appKeyName in subKey.GetSubKeyNames())
                        {
                            try
                            {
                                using var appKey = subKey.OpenSubKey(appKeyName);
                                if (appKey != null && IsSessionActive(appKey)) return true;
                            }
                            catch { }
                        }
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
            if (valueNames.Contains("LastUsedTimeStop") || valueNames.Contains("LastUsedTimeStart"))
            {
                object? stopVal = key.GetValue("LastUsedTimeStop");
                object? startVal = key.GetValue("LastUsedTimeStart");

                long stop = 0;
                long start = 0;

                if (stopVal is long sl) stop = sl;
                else if (stopVal != null) long.TryParse(stopVal.ToString(), out stop);

                if (startVal is long stl) start = stl;
                else if (startVal != null) long.TryParse(startVal.ToString(), out start);

                // If session started and has not stopped (stop == 0), or start is more recent than stop
                if (stop == 0 && start > 0) return true;
                if (start > stop) return true;
            }
        }
        catch { }

        return false;
    }
}
