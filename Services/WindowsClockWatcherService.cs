using System;
using System.IO;
using System.Windows.Threading;

namespace WindowsLive.Services;

public class WindowsClockTimerInfo
{
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
    public bool HasActiveTimer => _activeTimer != null && _activeTimer.IsRunning;

    public WindowsClockWatcherService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsFilePath = Path.Combine(localAppData, "Packages", PackageFamilyName, "Settings", "settings.dat");

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
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

            // Check if settings.dat was modified
            if (File.Exists(_settingsFilePath))
            {
                var writeTime = File.GetLastWriteTimeUtc(_settingsFilePath);
                if (force || writeTime != _lastSettingsFileTime)
                {
                    _lastSettingsFileTime = writeTime;
                    ReadClockSettings();
                }
            }
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
                        TimeSpan remaining = remainingTicks > 0 ? TimeSpan.FromTicks(remainingTicks) : duration;
                        DateTime updatedTime = updatedTicks > 0 ? DateTime.FromFileTimeUtc(updatedTicks) : DateTime.UtcNow;

                        // Check if timer was started or updated recently
                        var elapsedSinceUpdate = DateTime.UtcNow - updatedTime;
                        if (elapsedSinceUpdate < remaining && elapsedSinceUpdate >= TimeSpan.Zero)
                        {
                            var currentRemaining = remaining - elapsedSinceUpdate;
                            if (currentRemaining.TotalSeconds > 0)
                            {
                                latestRunning = new WindowsClockTimerInfo
                                {
                                    Name = name,
                                    Duration = duration,
                                    RemainingTime = currentRemaining,
                                    EndTime = DateTime.Now + currentRemaining,
                                    IsRunning = true
                                };
                                break;
                            }
                        }
                    }
                }
            }

            if (latestRunning != null)
            {
                _activeTimer = latestRunning;
                TimerStateChanged?.Invoke(_activeTimer);
            }
            else if (_activeTimer != null && !_activeTimer.IsRunning)
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

    public void CancelCurrentTimer()
    {
        _activeTimer = null;
        TimerStateChanged?.Invoke(null);
    }
}
