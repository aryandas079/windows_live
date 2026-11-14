using System;
using System.IO;
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

    private readonly DispatcherTimer _pollTimer;
    private WindowsClockTimerInfo? _activeTimer = null;
    private const string PackageFamilyName = "Microsoft.WindowsAlarms_8wekyb3d8bbwe";
    private readonly string _settingsFilePath;
    private DateTime _lastSettingsFileTime = DateTime.MinValue;

    public WindowsClockTimerInfo? ActiveTimer => _activeTimer;
    public bool HasActiveTimer => _activeTimer != null && (_activeTimer.IsRunning || _activeTimer.RemainingTime.TotalSeconds > 0);

    public WindowsClockWatcherService()
    {
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
        PollTimers(force: true);
        _pollTimer.Start();
    }

    private void PollTimers(bool force = false)
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

            // Always poll Windows Clock settings to detect live starts, pauses, resets, or cancellations in real time
            ReadClockSettings();
        }
        catch (Exception ex)
        {
            App.Log($"WindowsClockWatcherService PollTimers error: {ex.Message}");
        }
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
                    string name = comp.ContainsKey("Name") ? comp["Name"]?.ToString() ?? "Timer" : "Timer";
                    long durationTicks = comp.ContainsKey("Duration") && comp["Duration"] is long d ? d : 0;
                    long remainingTicks = comp.ContainsKey("RemainingTime") && comp["RemainingTime"] is long r ? r : 0;
                    long startTicks = comp.ContainsKey("StartTime") && comp["StartTime"] is long st ? st : 0;
                    long updatedTicks = comp.ContainsKey("__Updated") && comp["__Updated"] is long up ? up : 0;

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
            else if (_activeTimer != null)
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
        if (_activeTimer == null) return;
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
            _activeTimer.IsRunning = false;
            TimerStateChanged?.Invoke(_activeTimer);
        }
        catch (Exception ex)
        {
            App.Log($"WindowsClockWatcherService PauseActiveTimer error: {ex.Message}");
        }
    }

    public void ResumeActiveTimer()
    {
        if (_activeTimer == null) return;
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
            _activeTimer.IsRunning = true;
            _activeTimer.EndTime = DateTime.Now + _activeTimer.RemainingTime;
            TimerStateChanged?.Invoke(_activeTimer);
        }
        catch (Exception ex)
        {
            App.Log($"WindowsClockWatcherService ResumeActiveTimer error: {ex.Message}");
        }
    }

    public void CancelCurrentTimer()
    {
        if (_activeTimer != null)
        {
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

        _activeTimer = null;
        TimerStateChanged?.Invoke(null);
    }
}
