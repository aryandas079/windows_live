using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Threading;

namespace WindowsLive.Services;

public class VoiceRecordingInfo
{
    public bool IsRecording { get; set; }
    public bool IsPaused { get; set; }
    public bool IsCall { get; set; }
    public TimeSpan Duration { get; set; }
    public float PeakLevel { get; set; }
    public string AppName { get; set; } = "Microphone";
}

public class VoiceRecordingService
{
    public event Action<VoiceRecordingInfo>? RecordingStateChanged;
    public event Action<bool>? AnyCaptureStreamChanged;

    private readonly DispatcherTimer _pollTimer;
    private DateTime _recordingStartTime = DateTime.MinValue;
    private DateTime _lastActiveResumeTime = DateTime.MinValue;
    private TimeSpan _accumulatedDuration = TimeSpan.Zero;
    private bool _isRecording = false;
    private bool _isPaused = false;
    private bool _isDetectedCall = false;
    private string _activeAppName = "Microphone";
    private float _lastPeak = 0f;

    public bool IsRecording => _isRecording || _isSimulated;
    public bool IsPaused => _isPaused;
    public bool IsCallActive => IsRecording && _isDetectedCall;
    public TimeSpan CurrentDuration
    {
        get
        {
            if (!_isRecording && !_isSimulated) return TimeSpan.Zero;
            if (_isPaused) return _accumulatedDuration;
            return _accumulatedDuration + (DateTime.Now - _lastActiveResumeTime);
        }
    }
    public string ActiveAppName => _activeAppName;

    private readonly string[] _soundRecordDirs;
    private string? _lastAudioRecordFile;
    private long _lastAudioRecordSize = 0;
    private DateTime _lastAudioRecordTime = DateTime.MinValue;

    #region Win32 IPC & Input

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const int VK_SPACE = 0x20;

    #endregion

    #region COM Interop Definitions

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid id, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float pfPeak);
    }

    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(ref Guid AudioSessionGuid, uint StreamFlags, out IntPtr SessionControl);
        [PreserveSig]
        int GetSimpleAudioVolume(ref Guid AudioSessionGuid, uint StreamFlags, out IntPtr AudioVolume);
        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
    }

    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
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

    [Guid("F4B1A598-7269-43E5-A613-B415CEDE6124")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out AudioSessionState pRetVal);
    }

    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
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
        string myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        _soundRecordDirs = new[]
        {
            Path.Combine(myDocs, "Sound recordings"),
            Path.Combine(myDocs, "Sound Recordings"),
            Path.Combine(userProfile, "Documents", "Sound recordings"),
            Path.Combine(userProfile, "Documents", "Sound Recordings"),
            Path.Combine(userProfile, "OneDrive", "Documents", "Sound recordings"),
            Path.Combine(userProfile, "OneDrive", "Documents", "Sound Recordings"),
            Path.Combine(localAppData, "Packages", "Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe", "TempState"),
            Path.Combine(localAppData, "Packages", "Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe", "LocalState"),
            Path.Combine(localAppData, "Packages", "Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe", "LocalState", "Recordings"),
            Path.Combine(localAppData, "Packages", "Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe", "LocalState", "Metadata")
        };

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
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
            _isPaused = false;
            _isDetectedCall = false;
            _recordingStartTime = DateTime.Now;
            _lastActiveResumeTime = DateTime.Now;
            _accumulatedDuration = TimeSpan.Zero;
            _activeAppName = "Voice Recorder";
            RecordingStateChanged?.Invoke(new VoiceRecordingInfo
            {
                IsRecording = true,
                IsPaused = false,
                IsCall = false,
                Duration = TimeSpan.Zero,
                PeakLevel = 0.5f,
                AppName = _activeAppName
            });
        }
        else
        {
            _isRecording = false;
            _isPaused = false;
            _isDetectedCall = false;
            _accumulatedDuration = TimeSpan.Zero;
            RecordingStateChanged?.Invoke(new VoiceRecordingInfo
            {
                IsRecording = false,
                IsPaused = false,
                IsCall = false,
                Duration = TimeSpan.Zero,
                PeakLevel = 0f,
                AppName = _activeAppName
            });
        }
    }

    public void TogglePauseResume()
    {
        if (!_isRecording && !_isSimulated) return;
        bool targetPaused = !_isPaused;
        SetPaused(targetPaused);
        SendToggleToSoundRecorder();
    }

    public void StopActiveRecording()
    {
        _isSimulated = false;
        _isRecording = false;
        _isPaused = false;
        _accumulatedDuration = TimeSpan.Zero;
        _lastAudioRecordFile = null;

        RecordingStateChanged?.Invoke(new VoiceRecordingInfo
        {
            IsRecording = false,
            IsPaused = false,
            IsCall = false,
            Duration = TimeSpan.Zero,
            PeakLevel = 0f,
            AppName = string.Empty
        });

        try
        {
            Task.Run(() =>
            {
                try
                {
                    var procs = GetSoundRecorderProcesses();
                    if (procs.Length > 0)
                    {
                        int pid = procs[0].Id;
                        var condPid = new PropertyCondition(AutomationElement.ProcessIdProperty, pid);
                        var elements = AutomationElement.RootElement.FindAll(TreeScope.Subtree, condPid);
                        foreach (AutomationElement el in elements)
                        {
                            try
                            {
                                string name = el.Current.Name ?? string.Empty;
                                string autoId = el.Current.AutomationId ?? string.Empty;
                                if (name.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    autoId.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    if (el.TryGetCurrentPattern(InvokePattern.Pattern, out object patternObj) &&
                                        patternObj is InvokePattern inv)
                                    {
                                        inv.Invoke();
                                        App.Log($"VoiceRecordingService: UI Automation invoked Stop button '{name}'/'{autoId}'");
                                        return;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"VoiceRecordingService StopActiveRecording error: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            App.Log($"StopActiveRecording error: {ex.Message}");
        }
    }

    public void SetPaused(bool paused)
    {
        if (_isPaused == paused) return;
        _isPaused = paused;
        if (_isPaused)
        {
            _accumulatedDuration += DateTime.Now - _lastActiveResumeTime;
            App.Log($"VoiceRecordingService PAUSED. Duration frozen at {_accumulatedDuration.TotalSeconds:F1}s");
        }
        else
        {
            _lastActiveResumeTime = DateTime.Now;
            App.Log("VoiceRecordingService RESUMED");
        }

        RecordingStateChanged?.Invoke(new VoiceRecordingInfo
        {
            IsRecording = IsRecording,
            IsPaused = _isPaused,
            IsCall = _isDetectedCall,
            Duration = CurrentDuration,
            PeakLevel = _isPaused ? 0f : GetCurrentPeak(),
            AppName = _activeAppName
        });
    }

    private void SendToggleToSoundRecorder()
    {
        try
        {
            Task.Run(() =>
            {
                bool toggled = false;
                try
                {
                    var procs = GetSoundRecorderProcesses();
                    if (procs.Length > 0)
                    {
                        int pid = procs[0].Id;
                        var condPid = new PropertyCondition(AutomationElement.ProcessIdProperty, pid);
                        var elements = AutomationElement.RootElement.FindAll(TreeScope.Subtree, condPid);
                        foreach (AutomationElement el in elements)
                        {
                            try
                            {
                                string name = el.Current.Name ?? string.Empty;
                                string autoId = el.Current.AutomationId ?? string.Empty;
                                if (name.IndexOf("Pause", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    name.IndexOf("Resume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    name.IndexOf("Record", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    autoId.IndexOf("Pause", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    autoId.IndexOf("Resume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    autoId.IndexOf("Record", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    if (el.TryGetCurrentPattern(InvokePattern.Pattern, out object patternObj) &&
                                        patternObj is InvokePattern inv)
                                    {
                                        inv.Invoke();
                                        toggled = true;
                                        App.Log($"VoiceRecordingService: UI Automation invoked button '{name}'/'{autoId}'");
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"VoiceRecordingService UI Automation error: {ex.Message}");
                }

                if (!toggled)
                {
                    SendSpaceKeyToVoiceRecorder();
                }
            });
        }
        catch (Exception ex)
        {
            App.Log($"SendToggleToSoundRecorder error: {ex.Message}");
        }
    }

    private static void SendSpaceKeyToVoiceRecorder()
    {
        try
        {
            var procs = GetSoundRecorderProcesses();
            if (procs.Length == 0) return;
            int targetPid = procs[0].Id;

            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == targetPid)
                {
                    PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_SPACE, IntPtr.Zero);
                    PostMessage(hWnd, WM_KEYUP, (IntPtr)VK_SPACE, IntPtr.Zero);
                    return false;
                }

                var sbClass = new StringBuilder(256);
                GetClassName(hWnd, sbClass, 256);
                if (sbClass.ToString() == "ApplicationFrameWindow")
                {
                    EnumChildWindows(hWnd, (hChild, lP) =>
                    {
                        GetWindowThreadProcessId(hChild, out uint cPid);
                        if (cPid == targetPid)
                        {
                            PostMessage(hChild, WM_KEYDOWN, (IntPtr)VK_SPACE, IntPtr.Zero);
                            PostMessage(hChild, WM_KEYUP, (IntPtr)VK_SPACE, IntPtr.Zero);
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            App.Log($"SendSpaceKeyToVoiceRecorder error: {ex.Message}");
        }
    }

    private static Process[] GetSoundRecorderProcesses()
    {
        return Process.GetProcesses().Where(p =>
            p.ProcessName.Equals("VoiceRecorder", StringComparison.OrdinalIgnoreCase) ||
            p.ProcessName.Equals("SoundRec", StringComparison.OrdinalIgnoreCase) ||
            p.ProcessName.Equals("SoundRecorder", StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public void SimulateCall(string callerName, string appName)
    {
        _isSimulated = true;
        _isRecording = true;
        _isPaused = false;
        _isDetectedCall = true;
        _recordingStartTime = DateTime.Now;
        _lastActiveResumeTime = DateTime.Now;
        _accumulatedDuration = TimeSpan.Zero;
        _activeAppName = string.IsNullOrWhiteSpace(appName) ? "Phone" : appName;

        RecordingStateChanged?.Invoke(new VoiceRecordingInfo
        {
            IsRecording = true,
            IsPaused = false,
            IsCall = true,
            Duration = TimeSpan.Zero,
            PeakLevel = 0.5f,
            AppName = _activeAppName
        });
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
            return new MMDeviceEnumerator() as IMMDeviceEnumerator;
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
            RecordingStateChanged?.Invoke(new VoiceRecordingInfo
            {
                IsRecording = true,
                IsPaused = _isPaused,
                IsCall = _isDetectedCall,
                Duration = CurrentDuration,
                PeakLevel = _isPaused ? 0f : GetCurrentPeak(),
                AppName = _activeAppName
            });
            return;
        }

        try
        {
            bool anySessionActive = false;
            bool hasAnyCaptureStream = false;
            float currentPeak = GetCurrentPeak();
            string detectedApp = "Voice Recorder";
            bool isDetectedCall = false;
            bool isSoundRecPaused = false;
            bool voiceRecorderDetected = false;

            // 1. Direct check for Windows Sound Recorder / VoiceRecorder process
            var vrProcs = GetSoundRecorderProcesses();
            if (vrProcs.Length > 0)
            {
                if (IsSoundRecorderActivelyRecording(out isSoundRecPaused))
                {
                    voiceRecorderDetected = true;
                    anySessionActive = true;
                    detectedApp = "Voice Recorder";
                    isDetectedCall = false;
                }
            }

            var enumerator = CreateEnumerator();
            if (enumerator != null)
            {
                // eCapture = 1, eConsole = 0
                int hr = enumerator.GetDefaultAudioEndpoint(1, 0, out IMMDevice dev);
                if (hr == 0 && dev != null)
                {
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
                                        hasAnyCaptureStream = true;
                                    }

                                    sc2.GetProcessId(out int pid);
                                    if (pid > 0)
                                    {
                                        try
                                        {
                                            var proc = Process.GetProcessById(pid);
                                            string pName = proc.ProcessName;
                                            string title = proc.MainWindowTitle ?? string.Empty;

                                            // Exclude screen recording tools
                                            if (pName.IndexOf("SnippingTool", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                pName.IndexOf("ScreenSketch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                title.IndexOf("Recording toolbar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                title.IndexOf("Snipping Tool", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                pName.IndexOf("bcastdvr", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                pName.IndexOf("obs", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                SafeRelease(scObj);
                                                continue;
                                            }

                                            // Active voice/video calls
                                            if (state == AudioSessionState.AudioSessionStateActive)
                                            {
                                                if (pName.IndexOf("WhatsApp", StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                    detectedApp = "WhatsApp Voice";
                                                    isDetectedCall = true;
                                                    anySessionActive = true;
                                                    SafeRelease(scObj);
                                                    break;
                                                }
                                                else if (pName.IndexOf("Telegram", StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                    detectedApp = "Telegram Voice";
                                                    isDetectedCall = true;
                                                    anySessionActive = true;
                                                    SafeRelease(scObj);
                                                    break;
                                                }
                                                else if (pName.IndexOf("teams", StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                    detectedApp = "Microsoft Teams";
                                                    isDetectedCall = true;
                                                    anySessionActive = true;
                                                    SafeRelease(scObj);
                                                    break;
                                                }
                                                else if (pName.IndexOf("zoom", StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                    detectedApp = "Zoom Meeting";
                                                    isDetectedCall = true;
                                                    anySessionActive = true;
                                                    SafeRelease(scObj);
                                                    break;
                                                }
                                                else if (pName.IndexOf("skype", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         pName.IndexOf("lync", StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                    detectedApp = "Skype";
                                                    isDetectedCall = true;
                                                    anySessionActive = true;
                                                    SafeRelease(scObj);
                                                    break;
                                                }
                                                else if (pName.IndexOf("discord", StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                    detectedApp = "Discord";
                                                    isDetectedCall = true;
                                                    anySessionActive = true;
                                                    SafeRelease(scObj);
                                                    break;
                                                }
                                                else if (pName.IndexOf("PhoneExperienceHost", StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                    detectedApp = "Phone Link";
                                                    isDetectedCall = true;
                                                    anySessionActive = true;
                                                    SafeRelease(scObj);
                                                    break;
                                                }

                                                // Web browser calls
                                                if (pName.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                    pName.IndexOf("msedge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                    pName.IndexOf("firefox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                    pName.IndexOf("brave", StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                    string titleLower = title.ToLowerInvariant();
                                                    if (titleLower.Contains("meet") || titleLower.Contains("teams") ||
                                                        titleLower.Contains("zoom") || titleLower.Contains("webex") ||
                                                        titleLower.Contains("call") || titleLower.Contains("huddle"))
                                                    {
                                                        detectedApp = "Web Call";
                                                        isDetectedCall = true;
                                                        anySessionActive = true;
                                                        SafeRelease(scObj);
                                                        break;
                                                    }
                                                    SafeRelease(scObj);
                                                    continue;
                                                }

                                                // Audacity
                                                if (pName.IndexOf("Audacity", StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                    if (title.IndexOf("Recording", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        title.IndexOf("Rec", StringComparison.OrdinalIgnoreCase) >= 0)
                                                    {
                                                        detectedApp = "Audacity";
                                                        isDetectedCall = false;
                                                        anySessionActive = true;
                                                        SafeRelease(scObj);
                                                        break;
                                                    }
                                                    SafeRelease(scObj);
                                                    continue;
                                                }

                                                if (!string.IsNullOrWhiteSpace(title))
                                                {
                                                    string tLower = title.ToLowerInvariant();
                                                    if (tLower.Contains("call") || tLower.Contains("meeting") || tLower.Contains("huddle"))
                                                    {
                                                        detectedApp = title;
                                                        isDetectedCall = true;
                                                        anySessionActive = true;
                                                        SafeRelease(scObj);
                                                        break;
                                                    }
                                                }
                                            }

                                            // Sound Recorder check (regardless of Active/Inactive state)
                                            if (!voiceRecorderDetected &&
                                                (pName.IndexOf("SoundRec", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 pName.IndexOf("VoiceRecord", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 title.IndexOf("Sound Recorder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 title.IndexOf("Voice Recorder", StringComparison.OrdinalIgnoreCase) >= 0))
                                            {
                                                if (IsSoundRecorderActivelyRecording(out isSoundRecPaused))
                                                {
                                                    voiceRecorderDetected = true;
                                                    detectedApp = "Voice Recorder";
                                                    isDetectedCall = false;
                                                    anySessionActive = true;
                                                    SafeRelease(scObj);
                                                    break;
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                }
                                SafeRelease(scObj);
                            }
                            SafeRelease(se);
                        }
                        SafeRelease(sm);
                    }
                    SafeRelease(smObj);
                    SafeRelease(dev);
                }
                SafeRelease(enumerator);
            }

            // Fallback for ongoing Voice Recorder session when paused
            if (!anySessionActive && _isRecording && !_isDetectedCall)
            {
                if (vrProcs.Length > 0 && IsSoundRecorderActivelyRecording(out isSoundRecPaused))
                {
                    anySessionActive = true;
                    detectedApp = "Voice Recorder";
                }
            }

            AnyCaptureStreamChanged?.Invoke(hasAnyCaptureStream);

            if (anySessionActive)
            {
                _isDetectedCall = isDetectedCall;
                if (!_isRecording)
                {
                    _isRecording = true;
                    _isPaused = false;
                    _recordingStartTime = DateTime.Now;
                    _lastActiveResumeTime = DateTime.Now;
                    _accumulatedDuration = TimeSpan.Zero;
                    _activeAppName = detectedApp;
                    App.Log($"Voice recording started by '{_activeAppName}' (IsCall={isDetectedCall})");
                }

                if (isSoundRecPaused != _isPaused)
                {
                    SetPaused(isSoundRecPaused);
                }

                RecordingStateChanged?.Invoke(new VoiceRecordingInfo
                {
                    IsRecording = true,
                    IsPaused = _isPaused,
                    IsCall = isDetectedCall,
                    Duration = CurrentDuration,
                    PeakLevel = _isPaused ? 0f : currentPeak,
                    AppName = _activeAppName
                });
            }
            else
            {
                if (_isRecording)
                {
                    _isRecording = false;
                    _isPaused = false;
                    _isDetectedCall = false;
                    _accumulatedDuration = TimeSpan.Zero;
                    _lastAudioRecordFile = null;
                    App.Log("Voice recording ended");
                    RecordingStateChanged?.Invoke(new VoiceRecordingInfo
                    {
                        IsRecording = false,
                        IsPaused = false,
                        IsCall = false,
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

    private bool IsSoundRecorderActivelyRecording(out bool isPaused)
    {
        isPaused = false;
        foreach (var dir in _soundRecordDirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var dirInfo = new DirectoryInfo(dir);
                var recentFiles = dirInfo.GetFiles("*.*")
                    .Where(f =>
                    {
                        string ext = f.Extension.ToLowerInvariant();
                        return ext == ".m4a" || ext == ".mp3" || ext == ".wav" || ext == ".wma" || ext == ".tmp";
                    })
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(3);

                foreach (var file in recentFiles)
                {
                    double ageSec = (DateTime.Now - file.LastWriteTime).TotalSeconds;
                    bool isLocked = IsFileLocked(file.FullName);

                    // File is locked or was modified in the last 4 seconds
                    if (isLocked || ageSec < 4.0)
                    {
                        long currLen = file.Length;
                        if (file.FullName == _lastAudioRecordFile)
                        {
                            if (currLen > _lastAudioRecordSize)
                            {
                                _lastAudioRecordSize = currLen;
                                _lastAudioRecordTime = DateTime.Now;
                                isPaused = false;
                                return true;
                            }
                            else
                            {
                                double noGrowthSec = (DateTime.Now - _lastAudioRecordTime).TotalSeconds;
                                isPaused = noGrowthSec >= 1.5;
                                return true;
                            }
                        }
                        else
                        {
                            _lastAudioRecordFile = file.FullName;
                            _lastAudioRecordSize = currLen;
                            _lastAudioRecordTime = DateTime.Now;
                            isPaused = false;
                            return true;
                        }
                    }
                }
            }
            catch { }
        }

        // Keep recording session alive if currently paused and VoiceRecorder is still running
        if (_isRecording)
        {
            var vrProcs = GetSoundRecorderProcesses();
            if (vrProcs.Length > 0 && _isPaused)
            {
                isPaused = true;
                return true;
            }
        }

        return false;
    }

    private static bool IsFileLocked(string filePath)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SafeRelease(object? comObj)
    {
        if (comObj != null && Marshal.IsComObject(comObj))
        {
            try
            {
                Marshal.ReleaseComObject(comObj);
            }
            catch { }
        }
    }
}
