using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace WindowsLive.Services;

public class ScreenRecordingInfo
{
    public bool IsRecording { get; set; }
    public bool IsPaused { get; set; }
    public TimeSpan Duration { get; set; }
    public string AppName { get; set; } = "Screen Recording";
}

public class ScreenRecordingService
{
    public event Action<ScreenRecordingInfo>? RecordingStateChanged;

    private readonly DispatcherTimer _pollTimer;
    private DateTime _recordingStartTime = DateTime.MinValue;
    private DateTime _lastActiveResumeTime = DateTime.MinValue;
    private TimeSpan _accumulatedDuration = TimeSpan.Zero;
    private bool _isRecording = false;
    private bool _isPaused = false;
    private string _activeAppName = "Screen Recording";
    private bool _manualTestActive = false;

    public bool IsRecording => _isRecording || _manualTestActive;
    public bool IsPaused => _isPaused;
    public TimeSpan CurrentDuration
    {
        get
        {
            if (!_isRecording && !_manualTestActive) return TimeSpan.Zero;
            if (_isPaused) return _accumulatedDuration;
            return _accumulatedDuration + (DateTime.Now - _lastActiveResumeTime);
        }
    }
    public string ActiveAppName => _activeAppName;

    private readonly string _snippingToolTempDir;
    private readonly string _capturesDir;

    // Track active file growth for Snipping Tool & Game Bar to prevent false triggers when apps are only open
    private string? _lastSnippingFile;
    private long _lastSnippingSize = 0;
    private DateTime _lastSnippingTime = DateTime.MinValue;

    private string? _lastCapturesFile;
    private long _lastCapturesSize = 0;
    private DateTime _lastCapturesTime = DateTime.MinValue;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const int KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_LWIN = 0x5B;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_MENU = 0x12; // ALT
    private const byte VK_R = 0x52;

    public ScreenRecordingService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _snippingToolTempDir = Path.Combine(localAppData, "Packages", "Microsoft.ScreenSketch_8wekyb3d8bbwe", "TempState", "Recordings");

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _capturesDir = Path.Combine(userProfile, "Videos", "Captures");

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _pollTimer.Tick += (s, e) => PollScreenRecording();
    }

    public void Start()
    {
        App.Log("ScreenRecordingService started");
        PollScreenRecording();
        _pollTimer.Start();
    }

    public void ToggleManualTest()
    {
        _manualTestActive = !_manualTestActive;
        if (_manualTestActive)
        {
            _recordingStartTime = DateTime.Now;
            _lastActiveResumeTime = DateTime.Now;
            _accumulatedDuration = TimeSpan.Zero;
            _isPaused = false;
            _activeAppName = "Screen Recording";
        }
        else
        {
            _accumulatedDuration = TimeSpan.Zero;
            _isPaused = false;
        }
        PollScreenRecording();
    }

    public void TogglePauseResume()
    {
        if (!IsRecording) return;
        SetPaused(!_isPaused);
        SendToggleToScreenRecorder();
    }

    private void SendToggleToScreenRecorder()
    {
        try
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var procs = Process.GetProcessesByName("SnippingTool");
                    if (procs.Length > 0)
                    {
                        int pid = procs[0].Id;
                        var condPid = new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ProcessIdProperty, pid);
                        var elements = System.Windows.Automation.AutomationElement.RootElement.FindAll(System.Windows.Automation.TreeScope.Subtree, condPid);
                        foreach (System.Windows.Automation.AutomationElement el in elements)
                        {
                            try
                            {
                                string name = el.Current.Name ?? string.Empty;
                                string autoId = el.Current.AutomationId ?? string.Empty;
                                if (name.IndexOf("Pause", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    name.IndexOf("Resume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    autoId.IndexOf("Pause", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    autoId.IndexOf("Resume", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    if (el.TryGetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern, out object patternObj) &&
                                        patternObj is System.Windows.Automation.InvokePattern inv)
                                    {
                                        inv.Invoke();
                                        App.Log($"ScreenRecordingService: UI Automation invoked button '{name}'/'{autoId}'");
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
                    App.Log($"ScreenRecordingService UI Automation error: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            App.Log($"SendToggleToScreenRecorder error: {ex.Message}");
        }
    }

    public void SetPaused(bool paused)
    {
        if (_isPaused == paused) return;
        _isPaused = paused;
        if (_isPaused)
        {
            _accumulatedDuration += DateTime.Now - _lastActiveResumeTime;
            App.Log($"ScreenRecordingService PAUSED. Duration frozen at {_accumulatedDuration.TotalSeconds:F1}s");
        }
        else
        {
            _lastActiveResumeTime = DateTime.Now;
            App.Log("ScreenRecordingService RESUMED");
        }

        RecordingStateChanged?.Invoke(new ScreenRecordingInfo
        {
            IsRecording = IsRecording,
            IsPaused = _isPaused,
            Duration = CurrentDuration,
            AppName = _activeAppName
        });
    }

    public void StopActiveRecording()
    {
        if (_manualTestActive)
        {
            _manualTestActive = false;
            PollScreenRecording();
            return;
        }

        try
        {
            // If Snipping Tool recording is active, simulate Win+Shift+R to stop
            if (_activeAppName.Contains("Snipping", StringComparison.OrdinalIgnoreCase))
            {
                SimulateHotkey(VK_LWIN, VK_SHIFT, VK_R);
            }
            // If Game Bar is active, simulate Win+Alt+R to stop
            else if (_activeAppName.Contains("Game Bar", StringComparison.OrdinalIgnoreCase))
            {
                SimulateHotkey(VK_LWIN, VK_MENU, VK_R);
            }
            // If ffmpeg / ShareX is active
            else
            {
                var ffmpeg = Process.GetProcessesByName("ffmpeg").FirstOrDefault();
                if (ffmpeg != null)
                {
                    try { ffmpeg.CloseMainWindow(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"ScreenRecordingService StopActiveRecording error: {ex.Message}");
        }
    }

    private static void SimulateHotkey(byte mod1, byte mod2, byte key)
    {
        keybd_event(mod1, 0, 0, UIntPtr.Zero);
        keybd_event(mod2, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 0, UIntPtr.Zero);

        keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(mod2, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(mod1, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private void PollScreenRecording()
    {
        try
        {
            if (_manualTestActive)
            {
                RecordingStateChanged?.Invoke(new ScreenRecordingInfo
                {
                    IsRecording = true,
                    IsPaused = _isPaused,
                    Duration = CurrentDuration,
                    AppName = _activeAppName
                });
                return;
            }

            bool isRecordingDetected = false;
            bool isDetectedPaused = false;
            string detectedAppName = "Screen Recording";

            // 1. Check Windows 11 Snipping Tool active recording
            // Snipping tool writes to TempState\Recordings during active capture.
            // When user is merely viewing a preview or opening Snipping Tool, the file is NOT actively growing.
            if (Directory.Exists(_snippingToolTempDir))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(_snippingToolTempDir);
                    var recentFiles = dirInfo.GetFiles("*.mp4")
                        .Concat(dirInfo.GetFiles("*.mkv"))
                        .OrderByDescending(f => f.LastWriteTime)
                        .Take(2);

                    foreach (var file in recentFiles)
                    {
                        double ageSec = (DateTime.Now - file.LastWriteTime).TotalSeconds;
                        // File must have been touched recently or be locked by recording process
                        if (ageSec < 2.5 || IsFileLocked(file.FullName))
                        {
                            long currLen = file.Length;
                            if (file.FullName == _lastSnippingFile)
                            {
                                if (currLen > _lastSnippingSize)
                                {
                                    // File is actively growing in size: active, running recording!
                                    _lastSnippingSize = currLen;
                                    _lastSnippingTime = DateTime.Now;
                                    isRecordingDetected = true;
                                    isDetectedPaused = false;
                                    detectedAppName = "Snipping Tool";
                                    break;
                                }
                                else if (IsFileLocked(file.FullName))
                                {
                                    // Locked without growth for >= 2.0s indicates paused recording
                                    double noGrowth = (DateTime.Now - _lastSnippingTime).TotalSeconds;
                                    isRecordingDetected = true;
                                    isDetectedPaused = noGrowth >= 2.0;
                                    detectedAppName = "Snipping Tool";
                                    break;
                                }
                            }
                            else
                            {
                                _lastSnippingFile = file.FullName;
                                _lastSnippingSize = currLen;
                                _lastSnippingTime = DateTime.Now;
                                if (IsFileLocked(file.FullName))
                                {
                                    isRecordingDetected = true;
                                    isDetectedPaused = false;
                                    detectedAppName = "Snipping Tool";
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // 2. Check Videos\Captures folder for Xbox Game Bar active recording
            // Only consider it recording if a file is actively being written and locked by Game Bar
            if (!isRecordingDetected && Directory.Exists(_capturesDir))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(_capturesDir);
                    var recentFiles = dirInfo.GetFiles("*.mp4")
                        .Concat(dirInfo.GetFiles("*.mkv"))
                        .OrderByDescending(f => f.LastWriteTime)
                        .Take(2);

                    foreach (var file in recentFiles)
                    {
                        double ageSec = (DateTime.Now - file.LastWriteTime).TotalSeconds;
                        if ((ageSec < 2.5 || IsFileLocked(file.FullName)) && IsFileLocked(file.FullName))
                        {
                            long currLen = file.Length;
                            if (file.FullName == _lastCapturesFile)
                            {
                                if (currLen > _lastCapturesSize)
                                {
                                    _lastCapturesSize = currLen;
                                    _lastCapturesTime = DateTime.Now;
                                    isRecordingDetected = true;
                                    isDetectedPaused = false;
                                    detectedAppName = "Xbox Game Bar";
                                    break;
                                }
                                else
                                {
                                    double noGrowth = (DateTime.Now - _lastCapturesTime).TotalSeconds;
                                    isRecordingDetected = true;
                                    isDetectedPaused = noGrowth >= 2.0;
                                    detectedAppName = "Xbox Game Bar";
                                    break;
                                }
                            }
                            else
                            {
                                _lastCapturesFile = file.FullName;
                                _lastCapturesSize = currLen;
                                _lastCapturesTime = DateTime.Now;
                                isRecordingDetected = true;
                                isDetectedPaused = false;
                                detectedAppName = "Xbox Game Bar";
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            // 3. Check OBS Studio active recording
            // OBS Studio appends "[Recording: ...]" to its main window title only when recording is active.
            if (!isRecordingDetected)
            {
                var obs = Process.GetProcessesByName("obs64").Concat(Process.GetProcessesByName("obs32"));
                foreach (var p in obs)
                {
                    try
                    {
                        string title = p.MainWindowTitle;
                        if (!string.IsNullOrEmpty(title) &&
                            (title.IndexOf("Recording:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             title.IndexOf("[Recording", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            isRecordingDetected = true;
                            detectedAppName = "OBS Studio";
                            break;
                        }
                    }
                    catch { }
                }
            }

            // 4. Check ShareX screen recording
            if (!isRecordingDetected)
            {
                var ffmpegProcs = Process.GetProcessesByName("ffmpeg");
                if (ffmpegProcs.Length > 0)
                {
                    var sharex = Process.GetProcessesByName("ShareX");
                    if (sharex.Length > 0)
                    {
                        isRecordingDetected = true;
                        detectedAppName = "ShareX";
                    }
                }
            }

            // 5. Check other common screen recording processes by active window title (NOT mere process existence)
            if (!isRecordingDetected)
            {
                string[] knownRecorders = { "bdcam", "CamRecorder", "Loom", "Action", "D3DGear" };
                foreach (var name in knownRecorders)
                {
                    var procs = Process.GetProcessesByName(name);
                    foreach (var p in procs)
                    {
                        try
                        {
                            string title = p.MainWindowTitle;
                            if (!string.IsNullOrEmpty(title) &&
                                (title.IndexOf("Recording", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 title.IndexOf("Rec [", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                isRecordingDetected = true;
                                detectedAppName = name;
                                break;
                            }
                        }
                        catch { }
                    }
                    if (isRecordingDetected) break;
                }
            }

            if (isRecordingDetected)
            {
                if (!_isRecording)
                {
                    _isRecording = true;
                    _isPaused = false;
                    _recordingStartTime = DateTime.Now;
                    _lastActiveResumeTime = DateTime.Now;
                    _accumulatedDuration = TimeSpan.Zero;
                    _activeAppName = detectedAppName;
                    App.Log($"Screen recording started by '{_activeAppName}'");
                }

                if (isDetectedPaused != _isPaused)
                {
                    SetPaused(isDetectedPaused);
                }

                RecordingStateChanged?.Invoke(new ScreenRecordingInfo
                {
                    IsRecording = true,
                    IsPaused = _isPaused,
                    Duration = CurrentDuration,
                    AppName = _activeAppName
                });
            }
            else
            {
                if (_isRecording)
                {
                    _isRecording = false;
                    _isPaused = false;
                    _accumulatedDuration = TimeSpan.Zero;
                    App.Log("Screen recording ended");
                    RecordingStateChanged?.Invoke(new ScreenRecordingInfo
                    {
                        IsRecording = false,
                        IsPaused = false,
                        Duration = TimeSpan.Zero,
                        AppName = _activeAppName
                    });
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"ScreenRecordingService PollScreenRecording error: {ex.Message}");
        }
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
}
