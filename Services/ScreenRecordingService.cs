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
    public TimeSpan Duration { get; set; }
    public string AppName { get; set; } = "Screen Recording";
}

public class ScreenRecordingService
{
    public event Action<ScreenRecordingInfo>? RecordingStateChanged;

    private readonly DispatcherTimer _pollTimer;
    private DateTime _recordingStartTime = DateTime.MinValue;
    private bool _isRecording = false;
    private string _activeAppName = "Screen Recording";
    private bool _manualTestActive = false;

    public bool IsRecording => _isRecording || _manualTestActive;
    public TimeSpan CurrentDuration => (_isRecording || _manualTestActive)
        ? (DateTime.Now - _recordingStartTime)
        : TimeSpan.Zero;
    public string ActiveAppName => _activeAppName;

    private readonly string _snippingToolTempDir;
    private readonly string _capturesDir;

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
            _activeAppName = "Screen Recording";
        }
        PollScreenRecording();
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
                TimeSpan duration = DateTime.Now - _recordingStartTime;
                RecordingStateChanged?.Invoke(new ScreenRecordingInfo
                {
                    IsRecording = true,
                    Duration = duration,
                    AppName = _activeAppName
                });
                return;
            }

            bool isRecordingDetected = false;
            string detectedAppName = "Screen Recording";

            // 1. Check Windows 11 Snipping Tool active recording file
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
                        // File modified within the last 3.5 seconds or locked for writing
                        if ((DateTime.Now - file.LastWriteTime).TotalSeconds < 3.5 || IsFileLocked(file.FullName))
                        {
                            isRecordingDetected = true;
                            detectedAppName = "Snipping Tool";
                            break;
                        }
                    }
                }
                catch { }
            }

            // 2. Check Windows Game Bar Screen Recording (bcastdvr.exe)
            if (!isRecordingDetected)
            {
                var bcast = Process.GetProcessesByName("bcastdvr");
                if (bcast.Length > 0)
                {
                    isRecordingDetected = true;
                    detectedAppName = "Xbox Game Bar";
                }
            }

            // 3. Check OBS Studio active recording
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

            // 4. Check ShareX ffmpeg screen recording
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

            // 5. Check other common screen recording processes
            if (!isRecordingDetected)
            {
                string[] knownRecorders = { "bdcam", "CamRecorder", "Loom", "Action", "fraps", "D3DGear", "BBFlashBack" };
                foreach (var name in knownRecorders)
                {
                    var procs = Process.GetProcessesByName(name);
                    if (procs.Length > 0)
                    {
                        isRecordingDetected = true;
                        detectedAppName = name;
                        break;
                    }
                }
            }

            // 6. Check Videos\Captures folder for recent file actively being written
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
                        if ((DateTime.Now - file.LastWriteTime).TotalSeconds < 3.5 && IsFileLocked(file.FullName))
                        {
                            isRecordingDetected = true;
                            detectedAppName = "Screen Recording";
                            break;
                        }
                    }
                }
                catch { }
            }

            if (isRecordingDetected)
            {
                if (!_isRecording)
                {
                    _isRecording = true;
                    _recordingStartTime = DateTime.Now;
                    _activeAppName = detectedAppName;
                    App.Log($"Screen recording started by '{_activeAppName}'");
                }

                TimeSpan duration = DateTime.Now - _recordingStartTime;
                RecordingStateChanged?.Invoke(new ScreenRecordingInfo
                {
                    IsRecording = true,
                    Duration = duration,
                    AppName = _activeAppName
                });
            }
            else
            {
                if (_isRecording)
                {
                    _isRecording = false;
                    App.Log("Screen recording ended");
                    RecordingStateChanged?.Invoke(new ScreenRecordingInfo
                    {
                        IsRecording = false,
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
