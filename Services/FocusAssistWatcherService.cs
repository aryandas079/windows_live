using System;
using System.Windows.Threading;

namespace WindowsLive.Services;

public enum FocusAssistState
{
    Off,            // Unrestricted
    PriorityOnly,   // Priority Only
    AlarmsOnly      // Alarms Only
}

public class FocusAssistWatcherService
{
    public event Action<FocusAssistState>? FocusAssistChanged;

    private readonly DispatcherTimer _pollTimer;
    private FocusAssistState _lastState = FocusAssistState.Off;
    private bool _initialized = false;

    public FocusAssistState CurrentState => _lastState;

    public FocusAssistWatcherService()
    {
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _pollTimer.Tick += (s, e) => CheckState();
    }

    public void Start()
    {
        CheckState(initial: true);
        _pollTimer.Start();
    }

    private void CheckState(bool initial = false)
    {
        try
        {
            var manager = Windows.UI.Notifications.ToastNotificationManager.GetDefault();
            if (manager != null)
            {
                var mode = manager.NotificationMode;
                FocusAssistState state = mode switch
                {
                    Windows.UI.Notifications.ToastNotificationMode.PriorityOnly => FocusAssistState.PriorityOnly,
                    Windows.UI.Notifications.ToastNotificationMode.AlarmsOnly => FocusAssistState.AlarmsOnly,
                    _ => FocusAssistState.Off
                };

                if (!_initialized)
                {
                    _initialized = true;
                    _lastState = state;
                    return;
                }

                if (state != _lastState)
                {
                    _lastState = state;
                    FocusAssistChanged?.Invoke(state);
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"FocusAssistWatcherService CheckState error: {ex.Message}");
        }
    }
}
