using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Threading;

namespace WindowsLive.Services;

public class WindowsClockTimerInfo
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = "Timer";
    public TimeSpan Duration { get; set; }
    public TimeSpan RemainingTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsRunning { get; set; }
}

public class WindowsClockWatcherService
{
    public event Action<WindowsClockTimerInfo?>? TimerStateChanged;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _pollTimer;
    private WindowsClockTimerInfo? _activeTimer = null;
    private const string PackageFamilyName = "Microsoft.WindowsAlarms_8wekyb3d8bbwe";
    private readonly string _settingsFilePath;

    private AutomationElement? _cachedPauseBtn = null;
    private AutomationElement? _cachedStartBtn = null;
    private AutomationElement? _cachedResetBtn = null;

    private static readonly Regex TimeRegex = new Regex(@"^\d{1,2}:\d{2}(:\d{2})?$", RegexOptions.Compiled);

    public WindowsClockTimerInfo? ActiveTimer => _activeTimer;
    public bool HasActiveTimer => _activeTimer != null && (_activeTimer.IsRunning || _activeTimer.RemainingTime.TotalSeconds > 0);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public WindowsClockWatcherService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsFilePath = Path.Combine(localAppData, "Packages", PackageFamilyName, "Settings", "settings.dat");

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _pollTimer.Tick += (s, e) => PollTimers();
    }

    public void Start()
    {
        App.Log("WindowsClockWatcherService started");
        PollTimers();
        _pollTimer.Start();
    }

    private void PollTimers()
    {
        try
        {
            // If we have an active running timer, decrement remaining time smoothly
            if (_activeTimer != null && _activeTimer.IsRunning)
            {
                var remaining = _activeTimer.EndTime - DateTime.Now;
                if (remaining.TotalSeconds <= 0)
                {
                    _activeTimer.RemainingTime = TimeSpan.Zero;
                    _activeTimer.IsRunning = false;
                    TimerStateChanged?.Invoke(_activeTimer);
                    _activeTimer = null;
                    return;
                }
                else
                {
                    _activeTimer.RemainingTime = remaining;
                    TimerStateChanged?.Invoke(_activeTimer);
                }
            }

            // 1. Try to sync directly with Windows Clock UI
            bool syncedViaUI = TrySyncWithClockUI();

            // 2. Fall back to UWP settings
            if (!syncedViaUI)
            {
                ReadClockSettings();
            }
        }
        catch (Exception ex)
        {
            App.Log($"WindowsClockWatcherService PollTimers error: {ex.Message}");
        }
    }

    private static IntPtr FindClockWindowHandle()
    {
        var proc = Process.GetProcessesByName("Time");
        if (proc.Length == 0) return IntPtr.Zero;

        uint timePid = (uint)proc[0].Id;
        IntPtr foundHwnd = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == timePid)
            {
                foundHwnd = hWnd;
                return false;
            }

            var sbClass = new StringBuilder(256);
            GetClassName(hWnd, sbClass, 256);
            if (sbClass.ToString() == "ApplicationFrameWindow")
            {
                var sbTitle = new StringBuilder(256);
                GetWindowText(hWnd, sbTitle, 256);
                string title = sbTitle.ToString();
                if (title.Contains("Clock", StringComparison.OrdinalIgnoreCase))
                {
                    foundHwnd = hWnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        return foundHwnd;
    }

    private bool TrySyncWithClockUI()
    {
        try
        {
            IntPtr hWnd = FindClockWindowHandle();
            if (hWnd == IntPtr.Zero) return false;

            AutomationElement clockWindow = AutomationElement.FromHandle(hWnd);
            if (clockWindow == null) return false;

            // Find all buttons in Clock window
            var buttonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            var buttons = clockWindow.FindAll(TreeScope.Descendants, buttonCondition);

            AutomationElement? activePause = null;
            AutomationElement? activeStart = null;
            AutomationElement? activeReset = null;
            string? activeTimeString = null;
            string? activeTimerName = null;
            bool isRunning = false;

            // 1. Check for running timer (Button with Name="Pause")
            foreach (AutomationElement btn in buttons)
            {
                try
                {
                    string name = btn.Current.Name ?? string.Empty;
                    string aid = btn.Current.AutomationId ?? string.Empty;

                    if (name.Equals("Pause", StringComparison.OrdinalIgnoreCase) ||
                        aid.IndexOf("Pause", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        activePause = btn;
                        isRunning = true;
                        ExtractCardDetails(btn, out activeTimeString, out activeReset, out activeTimerName);
                        break;
                    }
                }
                catch { }
            }

            // 2. If no running timer, check for paused timer (Enabled "Reset" button)
            if (activePause == null)
            {
                foreach (AutomationElement btn in buttons)
                {
                    try
                    {
                        string name = btn.Current.Name ?? string.Empty;
                        string aid = btn.Current.AutomationId ?? string.Empty;

                        if ((name.Equals("Reset", StringComparison.OrdinalIgnoreCase) ||
                             aid.IndexOf("Reset", StringComparison.OrdinalIgnoreCase) >= 0) &&
                            btn.Current.IsEnabled)
                        {
                            activeReset = btn;
                            isRunning = false;
                            ExtractCardDetailsForReset(btn, out activeTimeString, out activeStart, out activeTimerName);
                            if (!string.IsNullOrEmpty(activeTimeString))
                            {
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }

            _cachedPauseBtn = activePause;
            _cachedStartBtn = activeStart;
            _cachedResetBtn = activeReset;

            if (!string.IsNullOrEmpty(activeTimeString) && TryParseTimerTime(activeTimeString, out TimeSpan remaining))
            {
                var resolved = new WindowsClockTimerInfo
                {
                    Key = "UIAutomationClockTimer",
                    Name = UwpResourceResolver.Clean(activeTimerName, "Timer"),
                    Duration = remaining,
                    RemainingTime = remaining,
                    EndTime = DateTime.Now + remaining,
                    IsRunning = isRunning
                };

                bool changed = _activeTimer == null ||
                               _activeTimer.IsRunning != resolved.IsRunning ||
                               Math.Abs((_activeTimer.RemainingTime - resolved.RemainingTime).TotalSeconds) > 1.5;

                if (changed || (_activeTimer != null && !_activeTimer.IsRunning))
                {
                    _activeTimer = resolved;
                    TimerStateChanged?.Invoke(_activeTimer);
                }
                return true;
            }
            else if (activePause == null && activeReset == null)
            {
                if (_activeTimer != null && _activeTimer.Key == "UIAutomationClockTimer")
                {
                    _activeTimer = null;
                    TimerStateChanged?.Invoke(null);
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"TrySyncWithClockUI error: {ex.Message}");
        }
        return false;
    }

    private static void ExtractCardDetails(AutomationElement actionBtn, out string? timeStr, out AutomationElement? resetBtn, out string? timerName)
    {
        timeStr = null;
        resetBtn = null;
        timerName = null;

        try
        {
            var parent = TreeWalker.ControlViewWalker.GetParent(actionBtn);
            if (parent == null) return;

            var texts = parent.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
            foreach (AutomationElement txt in texts)
            {
                try
                {
                    string val = txt.Current.Name ?? string.Empty;
                    if (IsTimeFormat(val))
                    {
                        timeStr = val;
                    }
                    else if (!string.IsNullOrWhiteSpace(val) && timerName == null &&
                             !val.Contains("AM", StringComparison.OrdinalIgnoreCase) &&
                             !val.Contains("PM", StringComparison.OrdinalIgnoreCase))
                    {
                        timerName = val;
                    }
                }
                catch { }
            }

            var btns = parent.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            foreach (AutomationElement b in btns)
            {
                try
                {
                    string bName = b.Current.Name ?? string.Empty;
                    string bAid = b.Current.AutomationId ?? string.Empty;
                    if (bName.Equals("Reset", StringComparison.OrdinalIgnoreCase) || bAid.IndexOf("Reset", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        resetBtn = b;
                        break;
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ExtractCardDetailsForReset(AutomationElement resetBtn, out string? timeStr, out AutomationElement? startBtn, out string? timerName)
    {
        timeStr = null;
        startBtn = null;
        timerName = null;

        try
        {
            var parent = TreeWalker.ControlViewWalker.GetParent(resetBtn);
            if (parent == null) return;

            var texts = parent.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
            foreach (AutomationElement txt in texts)
            {
                try
                {
                    string val = txt.Current.Name ?? string.Empty;
                    if (IsTimeFormat(val))
                    {
                        timeStr = val;
                    }
                    else if (!string.IsNullOrWhiteSpace(val) && timerName == null &&
                             !val.Contains("AM", StringComparison.OrdinalIgnoreCase) &&
                             !val.Contains("PM", StringComparison.OrdinalIgnoreCase))
                    {
                        timerName = val;
                    }
                }
                catch { }
            }

            var btns = parent.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            foreach (AutomationElement b in btns)
            {
                try
                {
                    string bName = b.Current.Name ?? string.Empty;
                    string bAid = b.Current.AutomationId ?? string.Empty;
                    if (bName.Equals("Start", StringComparison.OrdinalIgnoreCase) ||
                        bName.Equals("Play", StringComparison.OrdinalIgnoreCase) ||
                        bAid.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        startBtn = b;
                        break;
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static bool IsTimeFormat(string val)
    {
        if (string.IsNullOrWhiteSpace(val)) return false;
        return TimeRegex.IsMatch(val.Trim());
    }

    private static bool TryParseTimerTime(string str, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(str)) return false;
        str = str.Trim();
        var parts = str.Split(':');
        if (parts.Length == 3 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m) && int.TryParse(parts[2], out int s))
        {
            result = new TimeSpan(h, m, s);
            return true;
        }
        if (parts.Length == 2 && int.TryParse(parts[0], out int mins) && int.TryParse(parts[1], out int secs))
        {
            result = new TimeSpan(0, mins, secs);
            return true;
        }
        return false;
    }

    private void ReadClockSettings()
    {
        try
        {
            var appData = Windows.Management.Core.ApplicationDataManager.CreateForPackageFamily(PackageFamilyName);
            if (appData == null || appData.LocalSettings == null) return;

            if (!appData.LocalSettings.Containers.ContainsKey("Timers")) return;

            var timersContainer = appData.LocalSettings.Containers["Timers"];
            WindowsClockTimerInfo? latestRunning = null;
            WindowsClockTimerInfo? latestPaused = null;

            foreach (var pair in timersContainer.Values)
            {
                if (pair.Value is Windows.Storage.ApplicationDataCompositeValue comp)
                {
                    string name = UwpResourceResolver.Clean(comp.ContainsKey("Name") ? comp["Name"]?.ToString() : null, "Timer");
                    long durationTicks = comp.ContainsKey("Duration") && comp["Duration"] != null ? Convert.ToInt64(comp["Duration"]) : 0;
                    long remainingTicks = comp.ContainsKey("RemainingTime") && comp["RemainingTime"] != null ? Convert.ToInt64(comp["RemainingTime"]) : 0;
                    long startTicks = comp.ContainsKey("StartTime") && comp["StartTime"] != null ? Convert.ToInt64(comp["StartTime"]) : 0;

                    if (durationTicks > 0)
                    {
                        var duration = TimeSpan.FromTicks(durationTicks);

                        // Running state: StartTime > 0
                        if (startTicks > 0)
                        {
                            DateTime startTime = DateTime.FromFileTimeUtc(startTicks);
                            TimeSpan initialRemaining = remainingTicks > 0 ? TimeSpan.FromTicks(remainingTicks) : duration;
                            TimeSpan elapsed = DateTime.UtcNow - startTime;

                            if (elapsed >= TimeSpan.Zero && elapsed < initialRemaining)
                            {
                                var currentRemaining = initialRemaining - elapsed;
                                latestRunning = new WindowsClockTimerInfo
                                {
                                    Key = pair.Key,
                                    Name = name,
                                    Duration = duration,
                                    RemainingTime = currentRemaining,
                                    EndTime = DateTime.Now + currentRemaining,
                                    IsRunning = true
                                };
                                break;
                            }
                        }
                        // Paused state: StartTime == 0 and RemainingTime > 0
                        else if (remainingTicks > 0 && remainingTicks < durationTicks)
                        {
                            var remaining = TimeSpan.FromTicks(remainingTicks);
                            latestPaused = new WindowsClockTimerInfo
                            {
                                Key = pair.Key,
                                Name = name,
                                Duration = duration,
                                RemainingTime = remaining,
                                EndTime = DateTime.Now + remaining,
                                IsRunning = false
                            };
                        }
                    }
                }
            }

            var resolvedTimer = latestRunning ?? latestPaused;

            if (resolvedTimer != null)
            {
                bool changed = _activeTimer == null ||
                               _activeTimer.Key != resolvedTimer.Key ||
                               _activeTimer.IsRunning != resolvedTimer.IsRunning ||
                               Math.Abs((_activeTimer.RemainingTime - resolvedTimer.RemainingTime).TotalSeconds) > 1.5;

                if (changed || (_activeTimer != null && !_activeTimer.IsRunning))
                {
                    _activeTimer = resolvedTimer;
                    TimerStateChanged?.Invoke(_activeTimer);
                }
            }
            else if (_activeTimer != null && _activeTimer.Key != "UIAutomationClockTimer")
            {
                _activeTimer = null;
                TimerStateChanged?.Invoke(null);
            }
        }
        catch (Exception ex)
        {
            App.Log($"WindowsClockWatcherService ReadClockSettings error: {ex.Message}");
        }
    }

    public void PauseActiveTimer()
    {
        App.Log("WindowsClockWatcherService: PauseActiveTimer called");
        try
        {
            if (_cachedPauseBtn != null)
            {
                if (_cachedPauseBtn.GetCurrentPattern(InvokePattern.Pattern) is InvokePattern inv)
                {
                    inv.Invoke();
                    App.Log("WindowsClockWatcherService: Pause invoked via UI Automation");
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"PauseActiveTimer UIAutomation invoke error: {ex.Message}");
        }

        PauseStorageTimer();

        if (_activeTimer != null)
        {
            _activeTimer.IsRunning = false;
            TimerStateChanged?.Invoke(_activeTimer);
        }
    }

    public void ResumeActiveTimer()
    {
        App.Log("WindowsClockWatcherService: ResumeActiveTimer called");
        try
        {
            if (_cachedStartBtn != null)
            {
                if (_cachedStartBtn.GetCurrentPattern(InvokePattern.Pattern) is InvokePattern inv)
                {
                    inv.Invoke();
                    App.Log("WindowsClockWatcherService: Resume invoked via UI Automation");
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"ResumeActiveTimer UIAutomation invoke error: {ex.Message}");
        }

        ResumeStorageTimer();

        if (_activeTimer != null)
        {
            _activeTimer.IsRunning = true;
            _activeTimer.EndTime = DateTime.Now + _activeTimer.RemainingTime;
            TimerStateChanged?.Invoke(_activeTimer);
        }
    }

    public void CancelCurrentTimer()
    {
        App.Log("WindowsClockWatcherService: CancelCurrentTimer called");
        try
        {
            if (_cachedResetBtn != null)
            {
                if (_cachedResetBtn.GetCurrentPattern(InvokePattern.Pattern) is InvokePattern inv)
                {
                    inv.Invoke();
                    App.Log("WindowsClockWatcherService: Reset invoked via UI Automation");
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"CancelCurrentTimer UIAutomation invoke error: {ex.Message}");
        }

        CancelStorageTimer();

        _activeTimer = null;
        TimerStateChanged?.Invoke(null);
    }

    private void PauseStorageTimer()
    {
        if (_activeTimer == null || _activeTimer.Key == "UIAutomationClockTimer") return;
        try
        {
            var appData = Windows.Management.Core.ApplicationDataManager.CreateForPackageFamily(PackageFamilyName);
            if (appData?.LocalSettings?.Containers.ContainsKey("Timers") == true)
            {
                var timersContainer = appData.LocalSettings.Containers["Timers"];
                if (timersContainer.Values.ContainsKey(_activeTimer.Key) &&
                    timersContainer.Values[_activeTimer.Key] is Windows.Storage.ApplicationDataCompositeValue comp)
                {
                    comp["RemainingTime"] = _activeTimer.RemainingTime.Ticks;
                    comp["StartTime"] = 0L;
                    comp["__Updated"] = DateTime.UtcNow.ToFileTimeUtc();
                    timersContainer.Values[_activeTimer.Key] = comp;
                }
            }
        }
        catch { }
    }

    private void ResumeStorageTimer()
    {
        if (_activeTimer == null || _activeTimer.Key == "UIAutomationClockTimer") return;
        try
        {
            var appData = Windows.Management.Core.ApplicationDataManager.CreateForPackageFamily(PackageFamilyName);
            if (appData?.LocalSettings?.Containers.ContainsKey("Timers") == true)
            {
                var timersContainer = appData.LocalSettings.Containers["Timers"];
                if (timersContainer.Values.ContainsKey(_activeTimer.Key) &&
                    timersContainer.Values[_activeTimer.Key] is Windows.Storage.ApplicationDataCompositeValue comp)
                {
                    comp["StartTime"] = DateTime.UtcNow.ToFileTimeUtc();
                    comp["__Updated"] = DateTime.UtcNow.ToFileTimeUtc();
                    timersContainer.Values[_activeTimer.Key] = comp;
                }
            }
        }
        catch { }
    }

    private void CancelStorageTimer()
    {
        if (_activeTimer == null || _activeTimer.Key == "UIAutomationClockTimer") return;
        try
        {
            var appData = Windows.Management.Core.ApplicationDataManager.CreateForPackageFamily(PackageFamilyName);
            if (appData?.LocalSettings?.Containers.ContainsKey("Timers") == true)
            {
                var timersContainer = appData.LocalSettings.Containers["Timers"];
                if (timersContainer.Values.ContainsKey(_activeTimer.Key) &&
                    timersContainer.Values[_activeTimer.Key] is Windows.Storage.ApplicationDataCompositeValue comp)
                {
                    comp["StartTime"] = 0L;
                    comp["RemainingTime"] = 0L;
                    comp["__Updated"] = DateTime.UtcNow.ToFileTimeUtc();
                    timersContainer.Values[_activeTimer.Key] = comp;
                }
            }
        }
        catch { }
    }
}
