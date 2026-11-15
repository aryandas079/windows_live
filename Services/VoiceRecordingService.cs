using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace WindowsLive.Services;

public class VoiceRecordingInfo
{
    public bool IsRecording { get; set; }
    public TimeSpan Duration { get; set; }
    public float PeakLevel { get; set; }
    public string AppName { get; set; } = "Microphone";
}

public class VoiceRecordingService
{
    public event Action<VoiceRecordingInfo>? RecordingStateChanged;

    private readonly DispatcherTimer _pollTimer;
    private DateTime _recordingStartTime = DateTime.MinValue;
    private bool _isRecording = false;
    private string _activeAppName = "Microphone";
    private float _lastPeak = 0f;

    public bool IsRecording => _isRecording;
    public TimeSpan CurrentDuration => _isRecording ? (DateTime.Now - _recordingStartTime) : TimeSpan.Zero;
    public string ActiveAppName => _activeAppName;

    #region COM Interop Definitions

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid id, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float pfPeak);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(ref Guid AudioSessionGuid, uint StreamFlags, out IntPtr SessionControl);
        [PreserveSig]
        int GetSimpleAudioVolume(ref Guid AudioSessionGuid, uint StreamFlags, out IntPtr AudioVolume);
        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int SessionCount);
        [PreserveSig]
        int GetSession(int SessionIndex, [MarshalAs(UnmanagedType.IUnknown)] out object Session);
    }

    public enum AudioSessionState
    {
        AudioSessionStateInactive = 0,
        AudioSessionStateActive = 1,
        AudioSessionStateExpired = 2
    }

    [ComImport]
    [Guid("F4B1A598-7269-43E5-A613-B415CEDE6124"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out AudioSessionState pRetVal);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out AudioSessionState pRetVal);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);
        [PreserveSig] int GetGroupingParam(out Guid pRetVal);
        [PreserveSig] int SetGroupingParam([MarshalAs(UnmanagedType.LPStruct)] Guid Override, [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr NewNotifications);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr NewNotifications);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int GetProcessId(out int pRetVal);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference(bool optOut);
    }

    #endregion

    private IAudioMeterInformation? _cachedMeter;
    private readonly object _meterLock = new();
    private bool _isSimulated = false;
    private double _simPhase = 0.0;

    public VoiceRecordingService()
    {
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _pollTimer.Tick += (s, e) => PollMicrophoneState();
    }

    public void Start()
    {
        App.Log("VoiceRecordingService started");
        EnsureMeterInitialized();
        PollMicrophoneState();
        _pollTimer.Start();
    }

    public void SimulateToggle()
    {
        _isSimulated = !_isSimulated;
        if (_isSimulated)
        {
            _isRecording = true;
            _recordingStartTime = DateTime.Now;
            _activeAppName = "Voice Recorder";
            RecordingStateChanged?.Invoke(new VoiceRecordingInfo
            {
                IsRecording = true,
                Duration = TimeSpan.Zero,
                PeakLevel = 0.5f,
                AppName = _activeAppName
            });
        }
        else
        {
            _isRecording = false;
            RecordingStateChanged?.Invoke(new VoiceRecordingInfo
            {
                IsRecording = false,
                Duration = TimeSpan.Zero,
                PeakLevel = 0f,
                AppName = _activeAppName
            });
        }
    }

    public float GetCurrentPeak()
    {
        if (_isSimulated)
        {
            _simPhase += 0.22;
            // Simulated speech envelope with active bursts and natural pauses
            double burst = Math.Sin(_simPhase * 1.8) * Math.Sin(_simPhase * 0.45);
            return (float)Math.Clamp(Math.Abs(burst) * 0.85, 0.08, 0.95);
        }

        lock (_meterLock)
        {
            if (_cachedMeter == null)
            {
                EnsureMeterInitialized();
            }

            if (_cachedMeter != null)
            {
                try
                {
                    int hr = _cachedMeter.GetPeakValue(out float peak);
                    if (hr == 0 && peak >= 0f)
                    {
                        if (peak > 0f) _lastPeak = peak;
                        return peak;
                    }
                }
                catch { }
            }
            return _lastPeak;
        }
    }

    private static IMMDeviceEnumerator? CreateEnumerator()
    {
        try
        {
            Type? enumType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
            if (enumType == null) return null;
            return Activator.CreateInstance(enumType) as IMMDeviceEnumerator;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureMeterInitialized()
    {
        lock (_meterLock)
        {
            if (_cachedMeter != null) return;
            try
            {
                var enumerator = CreateEnumerator();
                if (enumerator == null) return;
                // eCapture = 1, eConsole = 0
                int hr = enumerator.GetDefaultAudioEndpoint(1, 0, out IMMDevice dev);
                if (hr == 0 && dev != null)
                {
                    var meterGuid = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
                    hr = dev.Activate(ref meterGuid, 1, IntPtr.Zero, out object meterObj);
                    if (hr == 0 && meterObj is IAudioMeterInformation meter)
                    {
                        _cachedMeter = meter;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"VoiceRecordingService EnsureMeterInitialized: {ex.Message}");
            }
        }
    }

    private void PollMicrophoneState()
    {
        if (_isSimulated)
        {
            TimeSpan simDur = DateTime.Now - _recordingStartTime;
            RecordingStateChanged?.Invoke(new VoiceRecordingInfo
            {
                IsRecording = true,
                Duration = simDur,
                PeakLevel = GetCurrentPeak(),
                AppName = _activeAppName
            });
            return;
        }

        try
        {
            bool anySessionActive = false;
            float currentPeak = GetCurrentPeak();
            string detectedApp = "Microphone";

            var enumerator = CreateEnumerator();
            if (enumerator == null) return;

            // eCapture = 1, eConsole = 0
            int hr = enumerator.GetDefaultAudioEndpoint(1, 0, out IMMDevice dev);
            if (hr == 0 && dev != null)
            {
                // Read capture sessions to detect active recording streams
                var smGuid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
                dev.Activate(ref smGuid, 1, IntPtr.Zero, out object smObj);
                if (smObj is IAudioSessionManager2 sm)
                {
                    int enumHr = sm.GetSessionEnumerator(out IAudioSessionEnumerator se);
                    if (enumHr == 0 && se != null)
                    {
                        se.GetCount(out int count);
                        for (int i = 0; i < count; i++)
                        {
                            object scObj;
                            se.GetSession(i, out scObj);
                            if (scObj is IAudioSessionControl2 sc2)
                            {
                                sc2.GetState(out AudioSessionState state);
                                if (state == AudioSessionState.AudioSessionStateActive)
                                {
                                    anySessionActive = true;
                                    sc2.GetProcessId(out int pid);
                                    if (pid > 0)
                                    {
                                        try
                                        {
                                            var proc = Process.GetProcessById(pid);
                                            string pName = proc.ProcessName;
                                            if (pName.IndexOf("SoundRec", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                pName.IndexOf("VoiceRecord", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                detectedApp = "Voice Recorder";
                                            }
                                            else if (pName.IndexOf("WhatsApp", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                detectedApp = "WhatsApp Voice";
                                            }
                                            else if (pName.IndexOf("Telegram", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                detectedApp = "Telegram Voice";
                                            }
                                            else if (pName.IndexOf("Audacity", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                detectedApp = "Audacity";
                                            }
                                            else if (pName.IndexOf("OBS", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                detectedApp = "OBS Recording";
                                            }
                                            else if (pName.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                detectedApp = "Chrome (Mic)";
                                            }
                                            else if (pName.IndexOf("msedge", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                detectedApp = "Edge (Mic)";
                                            }
                                            else if (pName.IndexOf("discord", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                detectedApp = "Discord";
                                            }
                                            else if (pName.IndexOf("teams", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                detectedApp = "Microsoft Teams";
                                            }
                                            else if (pName.IndexOf("zoom", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                detectedApp = "Zoom Meeting";
                                            }
                                            else if (!string.IsNullOrWhiteSpace(proc.MainWindowTitle))
                                            {
                                                detectedApp = proc.MainWindowTitle;
                                            }
                                            else
                                            {
                                                detectedApp = pName;
                                            }
                                        }
                                        catch { }
                                    }
                                    SafeRelease(scObj);
                                    break;
                                }
                            }
                            SafeRelease(scObj);
                        }
                    }
                    SafeRelease(se);
                }
                SafeRelease(smObj);
                SafeRelease(dev);
            }
            SafeRelease(enumerator);

            if (anySessionActive)
            {
                if (!_isRecording)
                {
                    _isRecording = true;
                    _recordingStartTime = DateTime.Now;
                    _activeAppName = detectedApp;
                    App.Log($"Voice recording started by '{_activeAppName}'");
                }

                TimeSpan duration = DateTime.Now - _recordingStartTime;
                RecordingStateChanged?.Invoke(new VoiceRecordingInfo
                {
                    IsRecording = true,
                    Duration = duration,
                    PeakLevel = currentPeak,
                    AppName = _activeAppName
                });
            }
            else
            {
                if (_isRecording)
                {
                    _isRecording = false;
                    App.Log("Voice recording ended");
                    RecordingStateChanged?.Invoke(new VoiceRecordingInfo
                    {
                        IsRecording = false,
                        Duration = TimeSpan.Zero,
                        PeakLevel = 0f,
                        AppName = _activeAppName
                    });
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"VoiceRecordingService PollMicrophoneState error: {ex.Message}");
        }
    }

    private static void SafeRelease(object? comObj)
    {
        if (comObj != null && Marshal.IsComObject(comObj))
        {
            try { Marshal.ReleaseComObject(comObj); } catch { }
        }
    }
}
