using System;
using System.Windows.Threading;

namespace WindowsLive.Services;

/// <summary>
/// Service that manages active ringing alarms, snooze states, and dismiss actions.
/// </summary>
public class AlarmService
{
    public event Action<bool, string>? AlarmStateChanged;
    public event Action<TimeSpan>? SnoozeStarted;

    private readonly DispatcherTimer _snoozeTimer;
    private bool _isRinging = false;
    private string _currentTitle = "Alarm";
    private uint? _activeNotificationId = null;

    public bool IsRinging => _isRinging;
    public string CurrentTitle => _currentTitle;
    public uint? ActiveNotificationId => _activeNotificationId;

    public AlarmService()
    {
        _snoozeTimer = new DispatcherTimer();
        _snoozeTimer.Tick += OnSnoozeTick;
    }

    /// <summary>
    /// Triggers an alarm ring event in the dock.
    /// </summary>
    public void TriggerAlarm(string title = "Alarm", uint? notifId = null)
    {
        _isRinging = true;
        _currentTitle = string.IsNullOrWhiteSpace(title) ? "Alarm" : title;
        _activeNotificationId = notifId;
        _snoozeTimer.Stop();

        App.Log($"AlarmService: Alarm ringing '{_currentTitle}' (NotifId: {_activeNotificationId})");
        AlarmStateChanged?.Invoke(true, _currentTitle);
    }

    /// <summary>
    /// Snoozes the current alarm for the specified duration (default 9 minutes).
    /// </summary>
    public void Snooze(int minutes = 9)
    {
        if (!_isRinging) return;

        _isRinging = false;
        App.Log($"AlarmService: Snoozed alarm '{_currentTitle}' for {minutes} min");
        AlarmStateChanged?.Invoke(false, _currentTitle);

        _snoozeTimer.Interval = TimeSpan.FromMinutes(minutes);
        _snoozeTimer.Start();
        SnoozeStarted?.Invoke(TimeSpan.FromMinutes(minutes));
    }

    /// <summary>
    /// Dismisses the alarm completely.
    /// </summary>
    public void Dismiss()
    {
        if (!_isRinging && !_snoozeTimer.IsEnabled) return;

        _isRinging = false;
        _snoozeTimer.Stop();
        App.Log($"AlarmService: Dismissed alarm '{_currentTitle}'");
        AlarmStateChanged?.Invoke(false, _currentTitle);
    }

    private void OnSnoozeTick(object? sender, EventArgs e)
    {
        _snoozeTimer.Stop();
        TriggerAlarm(_currentTitle, _activeNotificationId);
    }
}
